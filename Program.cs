using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

class BigFixed
{
    public BigInteger Value;
    public int Scale;

    public BigFixed(BigInteger v, int scale) { Value = v; Scale = scale; }

    public static BigFixed FromInt(long v, int scale) => new BigFixed(new BigInteger(v) * BigInteger.Pow(10, scale), scale);
    public static BigFixed FromBigInt(BigInteger v, int scale) => new BigFixed(v * BigInteger.Pow(10, scale), scale);

    public BigFixed Add(BigFixed other)
    {
        if (Scale != other.Scale) throw new ArgumentException("Scale mismatch");
        return new BigFixed(Value + other.Value, Scale);
    }

    public BigFixed Mul(BigFixed other)
    {
        if (Scale != other.Scale) throw new ArgumentException("Scale mismatch");
        var v = Value * other.Value;
        var factor = BigInteger.Pow(10, Scale);
        return new BigFixed((v + factor / 2) / factor, Scale);
    }

    public BigFixed MulInt(BigInteger k) => new BigFixed(Value * k, Scale);
    public BigFixed DivInt(BigInteger k) => new BigFixed((Value + k / 2) / k, Scale);

    public BigFixed Div(BigFixed other)
    {
        if (Scale != other.Scale) throw new ArgumentException("Scale mismatch");
        var factor = BigInteger.Pow(10, Scale);
        var num = Value * factor;
        return new BigFixed((num + other.Value / 2) / other.Value, Scale);
    }

    public override string ToString()
    {
        var s = BigInteger.Abs(Value).ToString();
        var sign = Value < 0 ? "-" : "";
        if (Scale == 0) return sign + s;
        if (s.Length <= Scale)
            s = s.PadLeft(Scale + 1, '0');

        var intPart = s.Substring(0, s.Length - Scale);
        var frac = s.Substring(s.Length - Scale).TrimEnd('0');
        return frac == "" ? sign + intPart : sign + intPart + "." + frac;
    }

    public BigFixed Sqrt(int iterations = 60)
    {
        if (Value < 0) throw new ArgumentException("Negative sqrt");
        if (Value == 0) return new BigFixed(0, Scale);

        var factor = BigInteger.Pow(10, Scale);
        var X = Value * factor;
        int approxDigits = (X.ToString().Length + 1) / 2;
        BigInteger g = BigInteger.Pow(10, Math.Max(1, approxDigits - 1));

        for (int i = 0; i < iterations; i++)
        {
            var gPrev = g;
            g = (g + X / g) / 2;
            if (BigInteger.Abs(g - gPrev) <= 1) break;
        }

        return new BigFixed(g, Scale);
    }
}

class ChudnovskyPi
{
    static readonly BigInteger A = 13591409;
    static readonly BigInteger B = 545140134;
    static readonly BigInteger C = 640320;

    static readonly Dictionary<int, BigInteger> factCache = new() { { 0, BigInteger.One }, { 1, BigInteger.One } };
    static readonly object factLock = new();

    static BigInteger Factorial(int n)
    {
        lock (factLock)
        {
            if (factCache.TryGetValue(n, out var v)) return v;

            int start = factCache.Keys.Max();
            BigInteger f = factCache[start];
            for (int i = start + 1; i <= n; i++)
            {
                f *= i;
                factCache[i] = f;
            }
            return factCache[n];
        }
    }

    public static string ComputePi(int digits)
    {
        if (digits < 1) throw new ArgumentException("digits must be >= 1");

        int extra = 10;
        int scale = digits + extra;
        int terms = (int)Math.Ceiling((digits + 2.0) / 14.181647462725477);

        for (int i = 0; i <= 6 * terms; i++) Factorial(i);

        var termsList = new BigInteger[terms];
        int completed = 0;

        Parallel.For(0, terms, k =>
        {
            int k6 = 6 * k;
            int k3 = 3 * k;
            BigInteger numFact = Factorial(k6);
            BigInteger den1 = Factorial(k3);
            BigInteger den2 = BigInteger.Pow(Factorial(k), 3);

            BigInteger multiplier = A + B * k;
            BigInteger sign = (k % 2 == 0) ? BigInteger.One : -BigInteger.One;

            BigInteger numerator = sign * numFact * multiplier;
            BigInteger denom = den1 * den2 * BigInteger.Pow(C, 3 * k);

            BigInteger scaledNum = BigInteger.Abs(numerator) * BigInteger.Pow(10, scale);
            BigInteger termValue = scaledNum / denom;
            if (numerator < 0) termValue = -termValue;

            termsList[k] = termValue;

            int done = Interlocked.Increment(ref completed);
            if (done % Math.Max(1, terms / 100) == 0)
            {
                double progress = 100.0 * done / terms;
                Console.Write($"\rProgress: {progress:F2}%   ");
            }
        });

        Console.WriteLine();

        BigFixed sum = new BigFixed(BigInteger.Zero, scale);
        foreach (var term in termsList)
            sum = sum.Add(new BigFixed(term, scale));

        BigFixed big10005 = BigFixed.FromBigInt(new BigInteger(10005), scale);
        BigFixed sqrt10005 = big10005.Sqrt(120);
        BigFixed multiplierBF = sqrt10005.MulInt(new BigInteger(426880));

        BigFixed pi = multiplierBF.Div(sum);

        var s = pi.ToString();
        if (!s.Contains("."))
        {
            if (digits == 0) return s;
            s = s + "." + new string('0', digits);
        }

        var parts = s.Split('.');
        var intPart = parts[0];
        var frac = parts.Length > 1 ? parts[1] : "";
        if (frac.Length < digits) frac = frac.PadRight(digits, '0');
        else if (frac.Length > digits) frac = frac.Substring(0, digits);

        return intPart + "." + frac;
    }
}

class Program
{
    static void PrintSystemInfo()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== System Information ===");
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"Machine Name: {Environment.MachineName}");
        Console.WriteLine($"OS: {Environment.OSVersion}");
        Console.WriteLine($".NET Version: {Environment.Version}");
        Console.WriteLine($"64-bit OS: {Environment.Is64BitOperatingSystem}");
        Console.WriteLine($"Processor Count: {Environment.ProcessorCount}");
        Console.ResetColor();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "select Name, NumberOfCores, NumberOfLogicalProcessors from Win32_Processor");
            foreach (var obj in searcher.Get())
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"CPU: {obj["Name"]}");
                Console.WriteLine($" - Physical cores: {obj["NumberOfCores"]}");
                Console.WriteLine($" - Logical processors: {obj["NumberOfLogicalProcessors"]}");
                Console.ResetColor();
            }
        }
        catch
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("(Detailed CPU info unavailable on this platform.)");
            Console.ResetColor();
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==========================\n");
        Console.ResetColor();
    }

    static void Main()
    {
        PrintSystemInfo();
        Console.WriteLine("Type 'pi <digits>' to compute pi, or 'exit' to quit.");

        while (true)
        {
            Console.Write("> ");
            string input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) continue;

            string[] parts = input.Trim().Split(' ');
            string command = parts[0].ToLower();

            if (command == "exit") break;

            else if (command == "pi")
            {
                if (parts.Length < 2 || !int.TryParse(parts[1], out int digits) || digits < 1)
                {
                    Console.WriteLine("Usage: pi <digits> (positive integer)");
                    continue;
                }

                Console.WriteLine($"Computing pi to {digits} decimal digits...");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                string pi = ChudnovskyPi.ComputePi(digits);
                sw.Stop();
                Console.WriteLine(pi);
                Console.WriteLine($"Done in {sw.Elapsed.TotalSeconds:F2}s");
            }
            else
            {
                Console.WriteLine("Unknown command. Available commands: pi, exit");
            }
        }
    }
}
