using Xunit;
using ForthToCsharp;
using System.IO;

using static System.Console;
using cell      = System.Int64;
using cellIndex = System.Int32;
using System.Collections.Generic;

namespace ForthToCsharp.Tests;

public class VmTest
{
    [Fact]
    public void StackManipulation()
    {
        var vm = new Vm(In, Out);

        vm.push(4);
        vm.push(3);
        vm.minus();
        Assert.Equal(1, vm.pop());

        vm.push(4);
        vm.push(3);
        Assert.Equal(3, vm.pop());
        Assert.Equal(4, vm.pop());
    }
    private cellIndex here(Vm vm) { vm.here(); return (cellIndex)vm.pop();}

    [Fact]
    public void DataSpaceManipulation()
    {
        var vm = new Vm(In, Out);

        // Can store and retrieve cells
        vm.push(1); var h1 = here(vm); vm.comma();
        vm.push(2); var h2 = here(vm); vm.comma(); //ds: h1 = 1, h2 = 2

        vm.push(h2); vm.at();
        vm.push(h1); vm.at();

        Assert.Equal(1, vm.pop());
        Assert.Equal(2, vm.pop());

        // Can store and retrieve chars
        vm.cpush('a'); var ha = here(vm); vm.comma();
        vm.cpush('b'); var hb = here(vm); vm.comma();

        vm.push(ha); vm.at();
        vm.push(hb); vm.at();

        Assert.Equal('b', vm.pop());
        Assert.Equal('a', vm.pop());

        // Aligned works
    }
    [Fact]
    public void RefillAndSource()
    {
        // TODO: a lot more to test here (empty string, various boundary values, ...)
        using TextReader tr = new StringReader("  bob cat dog   \n rob job\n");
        var vm = new Vm(tr, Out);
        var i = 0;
        do {
            vm.refill();
            vm.source();
            var res = i++ == 0 ? 16 : 8;
            Assert.Equal(res, vm.pop());
            vm.drop();
        } while(vm.pop() != vm.ffalse());
    }
    [Fact]
    public void Word()
    {
        using TextReader tr = new StringReader("  bob cat dog   \n rob job\nbib bop\n\n");
        var words = new string[] { "bob", "cat", "dog", "rob", "job", "bib", "bop"};

        var vm = new Vm(tr, Out);
        var lastLength = 0;
        int i = 0;
        do {
            vm.refill();
            do {
                vm.bl();
                vm.word();
                vm.count();
                vm.dup();
                lastLength = vm.popi();
                if(lastLength != 0) Assert.Equal(words[i++], vm.dotNetString());
            } while(lastLength != 0);
        } while(vm.pop() != vm.ffalse());
    }
}
