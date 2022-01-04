using static System.Console;

Vm vm = new Vm(In, Out);

WriteLine("## RUNNING TEST1.");
Forth.Test1(ref vm);

WriteLine("\n## RUNNING TEST2.");
Forth.Test2(ref vm);

WriteLine("\n## RUNNING ALL AGAIN.");
Forth.RunAll(ref vm);
