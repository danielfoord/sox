``` ini

BenchmarkDotNet=v0.12.0, OS=Windows 10.0.18363
AMD Ryzen 5 2600, 1 CPU, 12 logical and 6 physical cores
.NET Core SDK=3.1.200
  [Host]     : .NET Core 3.1.2 (CoreCLR 4.700.20.6602, CoreFX 4.700.20.6702), X64 RyuJIT
  DefaultJob : .NET Core 3.1.2 (CoreCLR 4.700.20.6602, CoreFX 4.700.20.6702), X64 RyuJIT


```
|                   Method |      Mean |     Error |    StdDev |
|------------------------- |----------:|----------:|----------:|
|   PackTextFrame1000Bytes | 10.222 us | 0.1145 us | 0.0894 us |
| PackBinaryFrame1000Bytes |  9.647 us | 0.1896 us | 0.2465 us |
