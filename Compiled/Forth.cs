#define NOBASEOFF
using System.Runtime.InteropServices;
using System;
using System.IO;
using System.Collections.Generic;

public class REAttribute : Attribute { };

/* This is the Forth VM in C#. It uses nint for cell values and int for index values.
 * This unfortunate difference is due to the fact that span cannot be indexed with nint.
 * Also, it uses 2 bytes for Char, as standard in .net. It could perhaps use UTF8 instead and translate
 * at the boundaries of the API.
 */

public delegate void ActionRef<T>(ref T item);

public struct Vm
{
    // Forth peculiar true and false.
    public const nint TRUE = -1;
    public const nint FALSE = 0;

    // You can't do sizeof nint at compile time in safe code.
    public const int CHAR_SIZE = sizeof(char);
    public readonly int CELL_SIZE = IntPtr.Size;

    // ps is the parameter stack, ds is the data space, rs is the return stack.
    public int top = 0;
    public int here_p = 0;
    public int rtop = 0;
    public nint[] ps;
    public byte[] ds;
    public nint[] rs;

    // xts for words
    public Dictionary<string, int> wordToXts = new();
    public ActionRef<Vm>[] xts;
    public int xtsp = 0;

    // Data space index of a word.
    public Dictionary<string, int> words = new();

    // Input/output buffer management.
    public TextWriter output;
    public TextReader input;
    public int source;
    public int word;

    public int inp = 0;
    public int source_max_chars;
    public int word_max_chars;
    public int input_len_chars = 0;

    // pad buffer.
    public int pad;

    // strings buffer.
    public int strings;

    // state: compiling -> true, interpreting -> false.
    public int state = 0;

    // Base management.
    public int base_p;

    public Vm(TextReader input,
              TextWriter output,
              int ps_max_cells = 64,
              int ds_max_bytes = 64 * 16 * 1_024,
              int rs_max_cells = 64,
              int source_max_chars = 1_024,
              int word_max_chars = 31,
              int xts_max = 64,
              int pad_max = 64,
              int strings_max_chars = 1_024
              )
    {

        ps = new nint[ps_max_cells * CELL_SIZE];
        ds = new byte[ds_max_bytes];
        rs = new nint[rs_max_cells * CELL_SIZE];

        xts = new ActionRef<Vm>[xts_max];
        this.input = input;
        this.output = output;

        word = here_p;
        here_p += word_max_chars * CHAR_SIZE;
        source = here_p;
        here_p += source_max_chars * CHAR_SIZE;

        pad = here_p;
        here_p += pad_max;

        this.source_max_chars = source_max_chars;
        this.word_max_chars = word_max_chars;

        base_p = here_p;
        here_p += CELL_SIZE;
        ds[base_p] = 10;

        strings = here_p;
        here_p += strings_max_chars * Vm.CHAR_SIZE;
    }
    public void reset() {
        // Don't restart the ds. Is that right?
        top = 0; rtop = 0; state = 0;
    }
}

public static partial class VmExt
{
    // This slow code is not called if you define NOBASE.
    // Also the code gets here if the user insert a word that could be interpreted as an hex number.
    private static nint ConvertToBase(ref Vm vm, string s) {
        var b = basev(ref vm);
        try {
            switch(vm.CELL_SIZE) {
                case 1: return (nint)Convert.ToSByte(s, b);
                case 2: return (nint)Convert.ToInt16(s, b);
                case 4: return (nint)Convert.ToInt32(s, b);
                case 8: return (nint)Convert.ToInt64(s, b);
            }
            throw new Exception($"{b} is not a valid base (supported 2, 8, 10 and 16).");
        }
        catch(Exception) {
            throw new Exception(
                    $"The word '{s}' was not found in the dictionary. Interpreting it as a number in base {b} failed as well.");
        }
    }
    [RE] public static Func<nint, nint, bool> loopCond(nint s, nint e) =>
        s < e ? (i, e1) => i < e1 : (i, e1) => i >= e1;
    [RE] public static void pushs(ref Vm vm, string s) => push(ref vm, ConvertToBase(ref vm, s));
    // Base manipulation.
    [RE] public static void basepu(ref Vm vm) => push(ref vm, vm.base_p);
    [RE] public static int basev(ref Vm vm) => vm.ds[vm.base_p];
    // Parameter Stack manipulation implementation routines. Not checking array boundaries as .net does it for me.
    [RE] public static void depth(ref Vm vm) => push(ref vm, vm.top);
    [RE] public static void push(ref Vm vm, nint c) => vm.ps[vm.top++] = c;
    [RE] public static nint pop(ref Vm vm) => vm.ps[--vm.top];
    [RE] public static int popi(ref Vm vm) => (int)vm.ps[--vm.top];
    [RE] public static void cpush(ref Vm vm, char c) => vm.ps[vm.top++] = (nint)c;
    [RE] public static char cpop(ref Vm vm) => (char)vm.ps[--vm.top];
    [RE] public static (nint, nint) pop2(ref Vm vm) => (vm.ps[--vm.top], vm.ps[--vm.top]);

    [RE] public static void cells(ref Vm vm) => push(ref vm, pop(ref vm) * vm.CELL_SIZE);
    [RE] public static void chars(ref Vm vm) => push(ref vm, pop(ref vm) * Vm.CHAR_SIZE);
    [RE] public static void charp(ref Vm vm) => push(ref vm, popi(ref vm) + Vm.CHAR_SIZE);
    [RE] public static void cellp(ref Vm vm) => push(ref vm, popi(ref vm) + vm.CELL_SIZE);
    [RE] public static void drop(ref Vm vm) => vm.top--;
    [RE] public static void drop2(ref Vm vm) => vm.top -= 2;
    [RE] public static void dup(ref Vm vm) => push(ref vm, vm.ps[vm.top - 1]);
    [RE] public static void dup2(ref Vm vm) { push(ref vm, vm.ps[vm.top - 2]); push(ref vm, vm.ps[vm.top - 2]);}
    [RE] public static void over(ref Vm vm) => push(ref vm, vm.ps[vm.top - 2]);
    [RE] public static void _qdup(ref Vm vm) {
        var t = pop(ref vm);
        if(t == Vm.FALSE) push(ref vm, Vm.FALSE);
        else { push(ref vm, t); push(ref vm, t);}
    }

    // Return stack manipulation.
    [RE] public static void rpush(ref Vm vm, nint c) => vm.rs[vm.rtop++] = c;
    [RE] public static nint rpop(ref Vm vm) => vm.rs[--vm.rtop];
    [RE] public static void toR(ref Vm vm) => rpush(ref vm, pop(ref vm));
    [RE] public static void fromR(ref Vm vm) => push(ref vm, rpop(ref vm));
    [RE] public static void fetchR(ref Vm vm) => push(ref vm, vm.rs[vm.rtop - 1]);

    // Data Space manipulation routines.
    [RE] public static void unused(ref Vm vm) => push(ref vm, vm.ds.Length - vm.here_p);
    [RE] public static void here(ref Vm vm) => push(ref vm, vm.here_p);
    [RE]
    public static void _fetch(ref Vm vm)
    {
        int c = popi(ref vm);
        var s = new Span<byte>(vm.ds, c, vm.CELL_SIZE);
        var value = MemoryMarshal.Read<nint>(s);
        push(ref vm, value);
    }
    [RE]
    public static void _store(ref Vm vm)
    {
        int c = popi(ref vm);
        var s = new Span<byte>(vm.ds, c, vm.CELL_SIZE);
        var v = pop(ref vm);
        MemoryMarshal.Write<nint>(s, ref v);
    }
    [RE]
    public static void _comma(ref Vm vm)
    {
        here(ref vm);
        _store(ref vm);
        vm.here_p += vm.CELL_SIZE;
    }
    [RE]
    public static void _cstore(ref Vm vm)
    {
        int c = popi(ref vm);
        var s = new Span<byte>(vm.ds, c, vm.CELL_SIZE);
        var v = (char)pop(ref vm);
        MemoryMarshal.Write<char>(s, ref v);
    }
    [RE]
    public static void _ccomma(ref Vm vm)
    {
        here(ref vm);
        _cstore(ref vm);
        vm.here_p += Vm.CHAR_SIZE;
    }
    [RE]
    public static void _cfetch(ref Vm vm)
    {
        int c = popi(ref vm);
        var s = new Span<byte>(vm.ds, c, Vm.CHAR_SIZE);
        var value = MemoryMarshal.Read<char>(s);
        push(ref vm, (nint)value);
    }
    [RE]
    public static void _fetchP(ref Vm vm)
    {
        var a = popi(ref vm);
        var n = pop(ref vm);

        var s = new Span<byte>(vm.ds, a, vm.CELL_SIZE);
        var value = MemoryMarshal.Read<nint>(s);

        push(ref vm, value + n);
        push(ref vm, a);
        _cstore(ref vm);
    }
    [RE]
    public static void allot(ref Vm vm)
    {
        var n = popi(ref vm);
        vm.here_p = vm.here_p + n;
    }
    private static int _align(int n, int alignment) => (n + (alignment - 1)) & ~(alignment - 1);
    [RE] public static void align(ref Vm vm) => vm.here_p = _align(vm.here_p, vm.CELL_SIZE);
    [RE] public static void aligned(ref Vm vm) { var n = popi(ref vm); push(ref vm, _align(n, vm.CELL_SIZE)); }

    // Memory manipulation.
    [RE] public static void move(ref Vm vm) {
        var c = popi(ref vm);
        var t = popi(ref vm);
        var f = popi(ref vm);
        Array.Copy(vm.ds, f, vm.ds, t, c);
    }
    [RE] public static void erase(ref Vm vm) {
        var c = popi(ref vm);
        var f = popi(ref vm);
        Array.Fill<byte>(vm.ds, 0, f, c);
    }
    [RE] public static void fill(ref Vm vm) {
        var v = (char)popi(ref vm);
        var c = popi(ref vm);
        var f = popi(ref vm);
        var s = MemoryMarshal.Cast<byte, char>(new Span<byte>(vm.ds, f, c * Vm.CHAR_SIZE));
        s.Fill(v);
    }
    [RE] public static void cmove(ref Vm vm) {
        var c = popi(ref vm);
        var t = popi(ref vm);
        var f = popi(ref vm);
        var s = MemoryMarshal.Cast<byte, char>(new Span<byte>(vm.ds, f, c * Vm.CHAR_SIZE));
        var k = MemoryMarshal.Cast<byte, char>(new Span<byte>(vm.ds, t, c * Vm.CHAR_SIZE));
        s.CopyTo(k);
    }
    [RE] public static void blank(ref Vm vm) {
        push(ref vm, (nint)' ');
        fill(ref vm);
    }

    // Input/Word manipulation routines.
    [RE]
    public static void type(ref Vm vm)
    {
        var l = popi(ref vm);
        var a = popi(ref vm);
        var chars = ToChars(ref vm, a, l);
        vm.output.Write(chars.ToString());
        vm.output.WriteLine();
    }

    [RE]
    public static void source(ref Vm vm)
    {
        push(ref vm, vm.source);
        push(ref vm, vm.input_len_chars);
    }
    [RE]
    public static void count(ref Vm vm)
    {
        dup(ref vm);
        _cfetch(ref vm);
        var c = popi(ref vm);
        var a = popi(ref vm);

        push(ref vm, a + Vm.CHAR_SIZE);
        push(ref vm, c);
    }
    [RE]
    public static void _fromDotNetString(ref Vm vm, string s, bool isCountedString) {
        var len = s.Length;
        var target = ToChars(ref vm, vm.strings, len + 1);
        var source = s.AsSpan();
        if(isCountedString) {
            target[0] = (char)len;
            source.CopyTo(target[1..]);
            push(ref vm, vm.strings);
        } else {
            source.CopyTo(target);
            push(ref vm, vm.strings);
            push(ref vm, len);
        }
    }
    [RE]
    public static string dotNetString(ref Vm vm)
    {
        var c = popi(ref vm);
        var a = popi(ref vm);
        var s = ToChars(ref vm, a, c);
        return s.ToString();
    }
    private static Span<Char> ToChars(ref Vm vm, int sourceIndex, int lengthInChars)
    {
        var inputByteSpan = vm.ds.AsSpan((int)sourceIndex, (int)lengthInChars * Vm.CHAR_SIZE);
        return MemoryMarshal.Cast<byte, char>(inputByteSpan);
    }
    [RE]
    public static void refill(ref Vm vm)
    {
        var s = vm.input.ReadLine();
        if (s == null)
        {
            push(ref vm, Vm.FALSE);
        }
        else
        {
            var len = s.Length;
            if (len > vm.source_max_chars)
                throw new Exception(
                $"Cannot parse a line longer than {vm.source_max_chars}. {s} is {len} chars long.");
            var inputCharSpan = ToChars(ref vm, vm.source, vm.source_max_chars);
            s.CopyTo(inputCharSpan);
            vm.inp = 0;
            vm.input_len_chars = len;
            push(ref vm, Vm.TRUE);
        }
    }
    [RE]
    public static void word(ref Vm vm)
    {
        var delim = (char)pop(ref vm);
        var s = ToChars(ref vm, vm.source, vm.input_len_chars);
        var w = ToChars(ref vm, vm.word, vm.word_max_chars);

        var j = 1; // It is a counted string, the first 2 bytes conains the length

        while (vm.inp < vm.input_len_chars && s[vm.inp] == delim) { vm.inp++; }

        // If all spaces to the end of the input, return a string with length 0. 
        if (vm.inp >= vm.input_len_chars)
        {
            w[0] = (char)0;
            push(ref vm, vm.word);
            return;
        }

        // Here i is the index to the first non-delim char, j indexes into the word buffer.
        while (j < vm.word_max_chars && vm.inp < vm.input_len_chars && s[vm.inp] != delim)
        {
            var c = s[vm.inp++];
            w[j++] = c;
        }
        if (j >= vm.input_len_chars) throw new Exception($"Word longer than {vm.input_len_chars}: {s}");

        w[0] = (char)(j - 1);  // len goes into the first char
        push(ref vm, vm.word);
    }
    [RE]
    public static void bl(ref Vm vm) => push(ref vm, (nint)' ');
    [RE]
    public static void nl(ref Vm vm) => push(ref vm, (nint)'\n');

    [RE]public static string _dotNetString(ref Vm vm)
    {
        var c = popi(ref vm);
        var a = popi(ref vm);
        var s = ToChars(ref vm, a, c);
        return s.ToString();
    }
    [RE]public static void _labelHere(ref Vm vm, string s) {
        vm.words[s] = vm.here_p;
    }
    [RE]public static void _pushLabel(ref Vm vm, string s) {
        push(ref vm, vm.words[s]);
    }
    [RE]public static void _pushLabelValue(ref Vm vm, string s, int literalCount) {
        var ptr = vm.words[s] + literalCount * vm.CELL_SIZE;
        push(ref vm, vm.ds[ptr]);
    }
    [RE] public static void _key(ref Vm vm) => push(ref vm, vm.input.Read());

    [RE] public static void _dots(ref Vm vm) {
        vm.output.Write($"<{vm.top}> ");
        for(int i = 0; i < vm.top; i++) { push(ref vm, vm.ps[i]); _dot(ref vm);}
        vm.output.WriteLine();
    }
    [RE] public static void _dot(ref Vm vm) {
        var n = pop(ref vm);
#if NOBASE
        vm.output.Write(n);
#else
        var b = vm.ds[vm.base_p];
        vm.output.Write(Convert.ToString(n, b));
#endif
        vm.output.Write(' ');
    }
    [RE] public static void urdot(ref Vm vm) {
        var a = pop(ref vm);
        var n = pop(ref vm);
#if NOBASE
        var s = string.Format($"{{0,{a}}}", n.ToString());
        vm.output.Write($"{s,a}");
#else
        var b = vm.ds[vm.base_p];
        var s = Convert.ToString(n, b);
        s = string.Format($"{{0,{a}}}", s);
        vm.output.Write($"{s}");
#endif
        vm.output.Write(' ');
    }
    [RE] public static void dump(ref Vm vm) {
        var n = popi(ref vm);
        var s = popi(ref vm);
        for(var i = 0; i < n; i++) {
            byte v = vm.ds[s + i];
            vm.output.Write($"{v:d},{v:x} ");
        }
        vm.output.WriteLine();
    }
}
public static class Forth {
public static void Test1(ref Vm vm) {

VmExt._labelHere(ref vm, "test");
vm.output.WriteLine("defining test as 1 + 1");
vm.output.WriteLine("defined.");

}
public static void Test2(ref Vm vm) {

do {

VmExt.pushs(ref vm, "1");
VmExt.pushs(ref vm, "1");
{
var a = VmExt.pop(ref vm);
var b = VmExt.pop(ref vm);
var c = b + a;
VmExt.push(ref vm, c);

}
} while(false);

vm.output.WriteLine("The value of test is ");
{
VmExt._dot(ref vm);;

}
vm.output.WriteLine("Done.");

}
public static void RunAll(ref Vm vm) {

Test1(ref vm);

Test2(ref vm);


}

}