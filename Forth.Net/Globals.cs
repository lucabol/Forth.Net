﻿#if CELL32
global using Cell      = System.Int32;
global using Index     = System.Int32;
#else
global using Cell      = System.Int64;
global using Index     = System.Int32;
#endif

global using Code    = System.Byte;
global using AUnit   = System.Byte;
global using AChar   = System.Byte;

global using System;
global using System.Globalization;
global using System.Text;
global using System.Runtime.CompilerServices;
global using System.Buffers.Binary;

global using System.Diagnostics.CodeAnalysis;
global using System.Reflection;
global using static Forth.Utils;

global using GitVarInt;


[assembly:InternalsVisibleTo("Forth.Net.Tests")]

namespace Forth;

public static class Config {
    const Index K                  = 1_024;
    public const Index SmallStack  = 16    * K;
    public const Index MediumStack = 256   * K;
    public const Index LargeStack  = 1_024 * K;
}
