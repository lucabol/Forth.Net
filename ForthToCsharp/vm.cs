using cell      = System.Int64;
using cellIndex = System.Int32;

using System.Runtime.InteropServices;
using static System.Console;
using System;
using System.IO;
using System.Collections.Generic;

public class REAttribute : Attribute { };

public class Vm
{
    /** Forth peculiar true and false **/
    const cell TRUE  = -1;
    const cell FALSE =  0;

    /** Sizes of primitive types **/
    const int CHAR_SIZE = sizeof(char);
    const int CELL_SIZE = sizeof(cell);

    /** Max values for the memory areas. **/
    const cellIndex PS_MAX      = 64 * CELL_SIZE;
    const cellIndex DS_MAX      = 64 * 1024;
    const cellIndex IN_MAX      = 512;
    const cellIndex WORD_MAX    = 64;

    /** ps is the parameter stack, ds is the data space **/
    cellIndex top    = 0;
    cellIndex here_p = 0;

    cell[] ps = new cell[PS_MAX];
    byte[] ds = new byte[DS_MAX];

    /** Need to store a corrispondence between xt and the functions they refer to. **/
    Dictionary<cellIndex, Action> xts = new Dictionary<cellIndex, Action>();

    public bool compiling;

    /** Input/output buffer management **/
    TextWriter _output;
    TextReader _input;
    cellIndex _source;
    cellIndex _in;
    cellIndex _inputLenInChars;

    /** word buffer management **/
    cellIndex _word;

    /** allocate a system array **/
    private cellIndex sysArray(cellIndex n) {
        var h = here_p;
        push(n);
        allot();
        return h;
    }
    public Vm(TextReader input, TextWriter output) {
        // Initialize input output buffers.
        _output = output;
        _input = input;
        _source = sysArray(IN_MAX * CHAR_SIZE);
        _in = 0;
        _inputLenInChars = 0;

        // Initialize word buffer.
        _word = sysArray(WORD_MAX * CHAR_SIZE);
    }

    /** Input/output area management **/
    [RE] public void type() {
        var l = popi();
        var a = popi();
        var chars = ToChars(a, l);
        _output.Write(chars.ToString());
    }

    [RE] public void source() {
        push(_source);
        push(_inputLenInChars);
    }
    [RE] public void count() {
        dup();
        cat();
        var c = popi();
        var a = popi();

        push(a + CHAR_SIZE);
        push(c);
    }
    public string dotNetString()
    {
        var c = popi();
        var a = popi();
        var s = ToChars(a, c);
        return s.ToString();
    }
    private Span<Char> ToChars(cellIndex sourceIndex, cellIndex lengthInChars) {
            var inputByteSpan = ds.AsSpan(sourceIndex, lengthInChars * CHAR_SIZE);
            return MemoryMarshal.Cast<byte, char>(inputByteSpan);
    }
    [RE] public void refill() {
        var s = _input.ReadLine();
        if(s == null) {
            push(FALSE);
        } else {
            var len = s.Length;
            if(len > IN_MAX) throw new Exception($"Cannot parse a line longer than {IN_MAX}. {s} is {len} chars long.");
            var inputCharSpan = ToChars(_source, IN_MAX);
            s.CopyTo(inputCharSpan);
            _in = 0;
            _inputLenInChars = len;
            push(TRUE);
        }
    }
    [RE] public void word() {
        var delim = (char)pop();
        var s = ToChars(_source, _inputLenInChars);
        var w = ToChars(_word, WORD_MAX);

        var j = 1; // It is a counted string, the first 2 bytes conains the length

        while(_in < _inputLenInChars && s[_in] == delim) { _in++; }

        // If all spaces to the end of the input, return a string with length 0. 
        if(_in >= _inputLenInChars) {
            w[0] = (char) 0;
            push(_word);
            return;
        }

        // Here i is the index to the first non-delim char, j indexes into the word buffer.
        while(j < WORD_MAX && _in < _inputLenInChars && s[_in] != delim ) {
            var c = s[_in++];
            w[j++] = c;
        }
        if(j >= WORD_MAX) throw new Exception($"Word longer than {WORD_MAX}: {s}");

        w[0] = (char) (j - 1);  // len goes into the first char
        push(_word);
    }
    [RE] public void bl() {
        push((cell)' ');
    }

    /** Pushing, popping and alloting from the data space **/
    [RE] public void comma() {
        cell c = pop();
        var end = here_p + CELL_SIZE;
        var s = new Span<byte>(ds, here_p, end);
        MemoryMarshal.Write<cell>(s, ref c);
        here_p = end;
    }
    [RE] public void at() {
        cellIndex c = popi();
        var s = new Span<byte>(ds, c, CELL_SIZE);
        var value = MemoryMarshal.Read<cell>(s);
        push(value);
    }
    [RE] public void ccomma() {
        char c = (char)pop();
        var end = here_p + CHAR_SIZE;
        var s = new Span<byte>(ds, here_p, end);
        MemoryMarshal.Write<char>(s, ref c);
        here_p = end;
    }
    [RE] public void cat() {
        cellIndex c = popi();
        var s = new Span<byte>(ds, c, CHAR_SIZE);
        var value = MemoryMarshal.Read<char>(s);
        push((cell)value);
    }
    [RE] public void allot() {
        var n = pop();
        here_p = (cellIndex)(here_p + n);
    }
    private cellIndex _align(cellIndex n, int alignment) => (n + (alignment -1)) & ~(alignment - 1);
    [RE] public void align() => here_p = _align(here_p, CELL_SIZE);
    [RE] public void aligned() { var n = (cellIndex) pop(); push(_align(n, CELL_SIZE)); }

    /** Let c# throw in case of out of memory or underflow. Perhaps the optimizer is happier
     * if I don't check it myself.
     **/
    public void cells() => push(CELL_SIZE);
    public void push(cell c) => ps[top++] = c;
    public cell pop() => ps[--top];
    public cellIndex popi() => (cellIndex)ps[--top];
    public void cpush(char c) => ps[top++] = (cell)c;
    public char cpop() => (char)ps[--top];
    public (cell, cell) pop2() => (ps[--top], ps[--top]);
    public cell ftrue() => TRUE;
    public cell ffalse() => FALSE;

    [RE] public void here() => push(here_p);
    [RE] public void drop() => pop();
    [RE] public void drop2() { pop(); pop();}
    [RE] public void dup() { var x = pop(); push(x); push(x);}
    [RE] public void dup2() { var (x, y) = (pop(), pop()); push(y);push(x);push(y);push(x);}

    [RE] public void plus() {
        var (b, a) = pop2();
        push(a + b);
    }
    [RE] public void minus() {
        var (b, a) = pop2();
        push(a - b);
    }
    [RE] public void state() => push(compiling ? TRUE : FALSE) ;

    [RE] public void dots() {
        Console.Write($"<{top}> ");
        for(int i = 0; i < top; i++) Console.Write($" {ps[i]}");
        Console.WriteLine();
    }
    [RE] public void dump() {
        var n = popi();
        var s = popi();
        for(var i = 0; i < n; i++) {
            var cellb = new Span<byte>(ds, s, CELL_SIZE);
            var v = MemoryMarshal.Read<cell>(cellb);
            _output.WriteLine($"{v,20:d} {v,20:x}");
        }
    }
}
