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
public struct Vm {
    // Forth peculiar true and false.
    public const nint TRUE  = -1;
    public const nint FALSE = 0;

    // You can't do sizeof nint at compile time in safe code.
    public const int    CHAR_SIZE = sizeof(char);
    public readonly int CELL_SIZE = IntPtr.Size;

    // ps is the parameter stack, ds is the data space.
    public int top    = 0;
    public int here_p = 0;
    public nint[] ps;
    public byte[] ds;

    // xts for words that has non standard does>.
    public Dictionary<int, Action> xts = new Dictionary<int, Action>();

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

    public Vm(int ps_max_cells,
              int ds_max_bytes,
              int source_max_chars,
              int word_max_chars,
              TextReader input,
              TextWriter output) {

        ps = new nint[ps_max_cells * CELL_SIZE];
        ds = new byte[ds_max_bytes];

        this.input  = input;
        this.output = output;

        word       = here_p;
        here_p    += word_max_chars * CHAR_SIZE;
        source     = here_p;
        here_p    += source_max_chars * CHAR_SIZE;

        this.source_max_chars = source_max_chars;
        this.word_max_chars   = word_max_chars;
    }
}

public static partial class VmExt {

    // Parameter Stack manipulation implementation routines. Not checking array boundaries as .net does it for me.
    [RE] private static void push(this ref Vm vm, nint c) => vm.ps[vm.top++] = c;
    [RE] private static nint pop(this ref Vm vm) => vm.ps[--vm.top];
    [RE] private static int popi(this ref Vm vm) => (int)vm.ps[--vm.top];
    [RE] private static void cpush(this ref Vm vm, char c) => vm.ps[vm.top++] = (nint)c;
    [RE] private static char cpop(this ref Vm vm) => (char)vm.ps[--vm.top];
    [RE] private static (nint, nint) pop2(this ref Vm vm) => (vm.ps[--vm.top], vm.ps[--vm.top]);

    // Private stack manipulation routines.
    [RE] public static void cells(this ref Vm vm) => vm.push(vm.CELL_SIZE);
    [RE] public static void drop(this ref Vm vm) => vm.pop();
    [RE] public static void drop2(this ref Vm vm) { vm.pop(); vm.pop();}
    [RE] public static void dup(this ref Vm vm) { var x = vm.pop(); vm.push(x); vm.push(x);}
    [RE] public static void dup2(this ref Vm vm) { var (x, y) = (vm.pop(), vm.pop()); vm.push(y);vm.push(x);vm.push(y);vm.push(x);}

    // Data Space manipulation routines.
    [RE] public static void here(this ref Vm vm) => vm.push(vm.here_p);
    [RE] public static void _fetch(this ref Vm vm) {
        int c = vm.popi();
        var s = new Span<byte>(vm.ds, c, vm.CELL_SIZE);
        var value = MemoryMarshal.Read<nint>(s);
        vm.push(value);
    }
    [RE] public static void _store(this ref Vm vm) {
        int c = vm.popi();
        var s = new Span<byte>(vm.ds, c, vm.CELL_SIZE);
        var v = vm.pop();
        MemoryMarshal.Write<nint>(s, ref v);
    }
    [RE] public static void _comma(this ref Vm vm) {
        vm.here();
        vm._store();
    }
    [RE] public static void _cstore(this ref Vm vm) {
        int c = vm.popi();
        var s = new Span<byte>(vm.ds, c, vm.CELL_SIZE);
        var v = (char)vm.pop();
        MemoryMarshal.Write<char>(s, ref v);
    }
    [RE] public static void _ccomma(this ref Vm vm) {
        vm.here();
        vm._cstore();
    }
    [RE] public static void _cfetch(this ref Vm vm) {
        int c = vm.popi();
        var s = new Span<byte>(vm.ds, c, Vm.CHAR_SIZE);
        var value = MemoryMarshal.Read<char>(s);
        vm.push((nint)value);
    }
    [RE] public static void allot(this ref Vm vm) {
        var n = vm.popi();
        vm.here_p = vm.here_p + n;
    }
    private static int _align(int n, int alignment) => (n + (alignment -1)) & ~(alignment - 1);
    [RE] public static void align(this ref Vm vm) => vm.here_p = _align(vm.here_p, vm.CELL_SIZE);
    [RE] public static void aligned(this ref Vm vm) { var n = vm.popi(); vm.push(_align(n, vm.CELL_SIZE)); }

    // Input/Word manipulation routines.

    /** Input/output area management **/
    [RE] public static void type(this ref Vm vm) {
        var l = vm.popi();
        var a = vm.popi();
        var chars = vm.ToChars(a, l);
        vm.output.Write(chars.ToString());
    }

    [RE] public static void source(this ref Vm vm) {
        vm.push(vm.source);
        vm.push(vm.input_len_chars);
    }
    [RE] public static void count(this ref Vm vm) {
        vm.dup();
        vm._cfetch();
        var c = vm.popi();
        var a = vm.popi();

        vm.push(a + Vm.CHAR_SIZE);
        vm.push(c);
    }
    public static string dotNetString(this ref Vm vm)
    {
        var c = vm.popi();
        var a = vm.popi();
        var s = vm.ToChars(a, c);
        return s.ToString();
    }
    private static Span<Char> ToChars(this ref Vm vm, int sourceIndex, int lengthInChars) {
            var inputByteSpan = vm.ds.AsSpan((int)sourceIndex, (int)lengthInChars * Vm.CHAR_SIZE);
            return MemoryMarshal.Cast<byte, char>(inputByteSpan);
    }
    [RE] public static void refill(this ref Vm vm) {
        var s = vm.input.ReadLine();
        if(s == null) {
            vm.push(Vm.FALSE);
        } else {
            var len = s.Length;
            if(len > vm.source_max_chars)
                throw new Exception(
                $"Cannot parse a line longer than {vm.source_max_chars}. {s} is {len} chars long.");
            var inputCharSpan = vm.ToChars(vm.source, vm.source_max_chars);
            s.CopyTo(inputCharSpan);
            vm.inp = 0;
            vm.input_len_chars = len;
            vm.push(Vm.TRUE);
        }
    }
    [RE] public static void word(this ref Vm vm) {
        var delim = (char)vm.pop();
        var s = vm.ToChars(vm.source, vm.input_len_chars);
        var w = vm.ToChars(vm.word, vm.word_max_chars);

        var j = 1; // It is a counted string, the first 2 bytes conains the length

        while(vm.inp < vm.input_len_chars && s[vm.inp] == delim) { vm.inp++; }

        // If all spaces to the end of the input, return a string with length 0. 
        if(vm.inp >= vm.input_len_chars) {
            w[0] = (char) 0;
            vm.push(vm.word);
            return;
        }

        // Here i is the index to the first non-delim char, j indexes into the word buffer.
        while(j < vm.word_max_chars && vm.inp < vm.input_len_chars && s[vm.inp] != delim ) {
            var c = s[vm.inp++];
            w[j++] = c;
        }
        if(j >= vm.input_len_chars) throw new Exception($"Word longer than {vm.input_len_chars}: {s}");

        w[0] = (char) (j - 1);  // len goes into the first char
        vm.push(vm.word);
    }
    [RE] public static void bl(this ref Vm vm) {
        vm.push((nint)' ');
    }
}
