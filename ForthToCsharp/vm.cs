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


public delegate void Doer(ref Vm vm);

public struct Vm
{
    // Forth peculiar true and false.
    public const nint TRUE = -1;
    public const nint FALSE = 0;

    // You can't do sizeof nint at compile time in safe code.
    public const int CHAR_SIZE = sizeof(char);
    public readonly int CELL_SIZE = IntPtr.Size;

    // ps is the parameter stack, ds is the data space.
    public int top = 0;
    public int here_p = 0;
    public nint[] ps;
    public byte[] ds;

    // Defined words need to store their doer behvior as their name might change at runtime.
    public Dictionary<string, Doer> doers = new();

    // xts for words that has non standard does>.
    public Dictionary<int, string> xts = new();

    // Data space index of a word.
    public Dictionary<string, int> words = new();

    // Input/output buffer management.
    public TextWriter output;
    public TextReader input;
    public int source;

    public int inp = 0;
    public int source_max_chars;
    public int word_max_chars;
    public int input_len_chars = 0;

    // Word buffer management.
    public int word;

    // state: compiling -> true, interpreting -> false.
    public int state = 0;

    public Vm(TextReader input,
              TextWriter output,
              int ps_max_cells = 64,
              int ds_max_bytes = 64 * 1_024,
              int source_max_chars = 1_024,
              int word_max_chars = 31)
    {

        ps = new nint[ps_max_cells * CELL_SIZE];
        ds = new byte[ds_max_bytes];

        this.input = input;
        this.output = output;

        word = here_p;
        here_p += word_max_chars * CHAR_SIZE;
        source = here_p;
        here_p += source_max_chars * CHAR_SIZE;

        this.source_max_chars = source_max_chars;
        this.word_max_chars = word_max_chars;
    }
}

public static partial class VmExt
{

    // Parameter Stack manipulation implementation routines. Not checking array boundaries as .net does it for me.
    [RE] public static void depth(ref Vm vm) => push(ref vm, vm.top);
    [RE] public static void push(ref Vm vm, nint c) => vm.ps[vm.top++] = c;
    [RE] public static nint pop(ref Vm vm) => vm.ps[--vm.top];
    [RE] public static int popi(ref Vm vm) => (int)vm.ps[--vm.top];
    [RE] public static void cpush(ref Vm vm, char c) => vm.ps[vm.top++] = (nint)c;
    [RE] public static char cpop(ref Vm vm) => (char)vm.ps[--vm.top];
    [RE] public static (nint, nint) pop2(ref Vm vm) => (vm.ps[--vm.top], vm.ps[--vm.top]);

    // Private stack manipulation routines.
    [RE] public static void cells(ref Vm vm) => push(ref vm, vm.CELL_SIZE);
    [RE] public static void drop(ref Vm vm) => pop(ref vm);
    [RE] public static void drop2(ref Vm vm) { pop(ref vm); pop(ref vm); }
    [RE] public static void dup(ref Vm vm) { var x = pop(ref vm); push(ref vm, x); push(ref vm, x); }
    [RE] public static void dup2(ref Vm vm) { var (x, y) = (pop(ref vm), pop(ref vm)); push(ref vm, y); push(ref vm, x); push(ref vm, y); push(ref vm, x); }

    // Data Space manipulation routines.
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
    public static void allot(ref Vm vm)
    {
        var n = popi(ref vm);
        vm.here_p = vm.here_p + n;
    }
    private static int _align(int n, int alignment) => (n + (alignment - 1)) & ~(alignment - 1);
    [RE] public static void align(ref Vm vm) => vm.here_p = _align(vm.here_p, vm.CELL_SIZE);
    [RE] public static void aligned(ref Vm vm) { var n = popi(ref vm); push(ref vm, _align(n, vm.CELL_SIZE)); }

    // Input/Word manipulation routines.

    /** Input/output area management **/
    [RE]
    public static void type(ref Vm vm)
    {
        var l = popi(ref vm);
        var a = popi(ref vm);
        var chars = ToChars(ref vm, a, l);
        vm.output.Write(chars.ToString());
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
    public static void bl(ref Vm vm)
    {
        push(ref vm, (nint)' ');
    }
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

    [RE]public static void _do(ref Vm vm, string word) {
        if(vm.doers.TryGetValue(word, out var f)) f(ref vm);
        else if(vm.words.TryGetValue(word, out var a)) push(ref vm, a);
        else throw new Exception($"{word} is not a word in the dictionary");
    }
}
