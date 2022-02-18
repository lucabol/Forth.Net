using Xunit;
using Forth;
using System.Collections.Generic;
using System.Linq;
using System;
using static Forth.Utils;

namespace LForth.Net.Tests;

public class UnitTest1
{
    [Theory]
    [MemberData(nameof(GetData7Bit))]
    public void CanDoBit7Conversion(long n, long many) {
        var array = new byte[10];
        Write7BitEncodedCell(array, 0, n, out var howManyRead);
        var n1 = Read7BitEncodedCell(array, 0, out var howManyWritten);
        Assert.Equal(n, n1);
        Assert.Equal(many, howManyRead);
        Assert.Equal(many, howManyWritten);
    }
    [Theory]
    [MemberData(nameof(GetDataPush))]
    public void PushAndPopWork(long n) {
        var vm = new Vm();
        vm.Push(n);
        Assert.Equal(n, vm.Pop());
        vm.Push(n);
        vm.Push(0);
        vm.Pop();
        Assert.Equal(n, vm.Pop());

        EmptyStack(vm);
    }
    [Fact]
    public void DupWorks()
    {
        Vm vm = new();
        vm.Push(3);
        vm.Dup();
        Assert.Equal(3, vm.Pop());
        Assert.Equal(3, vm.Pop());

        vm.Push(3);
        vm.Push(2);
        vm.Dup2();
        Assert.Equal(2, vm.Pop());
        Assert.Equal(3, vm.Pop());
        Assert.Equal(2, vm.Pop());
        Assert.Equal(3, vm.Pop());
        EmptyStack(vm);
    }
    [Fact]
    public void ResizeStackWorks()
    {
        Vm vm = new(parameterStackSize: 10);
        for (long i = 0; i < 20; i++)
            vm.Push(i);
        for (long i = 19; i >= 0; i--)
            Assert.Equal(i, vm.Pop());

        EmptyStack(vm);
    }
    [Theory]
    [MemberData(nameof(GetDataRefill))]
    public void RefillWorks(string s)
    {
        var vm = new Vm { NextLine = () => s };
        vm.Refill();
        var flag = vm.Pop();
        Assert.Equal(-1, flag);
        vm.Source();
        Assert.Equal(s.Trim(), vm.ToDotNetString());
        
        EmptyStack(vm);
    }
    [Theory]
    [MemberData(nameof(GetDataWord))]
    public void WordWorks(string s, string[] words)
    {
        var vm = new Vm { NextLine = () => s };
        vm.Refill();
        var flag = vm.Pop();
        Assert.Equal(-1, flag);

        var i = 0;
        while(true)
        {
            vm.Push(' ');
            vm.WordW();
            if(vm.IsEmptyWordC()) { vm.Drop(); break;};
            Assert.Equal(words[i], vm.ToDotNetStringC());
            i++;
        }

        EmptyStack(vm);
    }
    [Fact]
    public void DataSpaceWorks()
    {
        var vm = new Vm();
        vm.Here();
        var here = vm.Pop();

        vm.Push(10);
        vm.Comma();
        vm.Push(here);
        vm.At();
        Assert.Equal(10, vm.Pop());

        EmptyStack(vm);
    }
    [Fact]
    public void DictWorks()
    {
        var vm = new Vm();
        string s = "bobo rob cb d";
        vm.NextLine = () => s;
        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries |
                                      StringSplitOptions.TrimEntries);
        vm.Refill();
        vm.Drop();

        foreach(var w in words) {
            vm.Bl();
            vm.WordW();
            vm.DictAdd();
        }
        vm.Refill();
        vm.Drop();

        foreach(var w in words)
        {
           vm.Bl();
           vm.WordW();
           vm.FindUserDefinedWord();
           Assert.Equal(-1, vm.Pop());
           vm.Drop(); // Dropping the xt
        }

        EmptyStack(vm);
    }
    static void EmptyStack(Vm vm) {
        vm.Depth();
        Assert.Equal(0, vm.Pop());
    }
    public static IEnumerable<object[]> GetData7Bit() =>
        new (long, long)[] { (0, 1), (-1, 1), (+1, 1), (-900, 2), (-100_000, 3), (long.MaxValue, 10), (long.MinValue, 10) }
        .Select(t => new object[] {t.Item1, t.Item2});
    public static IEnumerable<object[]> GetDataPush() =>
            new long[] {-1, 0, 1, 2, 3, long.MaxValue, long.MinValue}.Select(o => new object[] { o });
    public static IEnumerable<object[]> GetDataRefill() =>
            new string[] {"", "a", " ab bb "}.Select(o => new object[] { o });
    public static IEnumerable<object[]> GetDataWord() =>
            new (string, string[])[] {("",new string[] {""}), ("a", new string[]{"a"}), ("ab   ", new string[]{"ab"}),
                (" ab", new string[]{"ab"}), ("  ab bb  ", new string[] {"ab", "bb"})}.Select(t => new object[] { t.Item1, t.Item2 });
}
