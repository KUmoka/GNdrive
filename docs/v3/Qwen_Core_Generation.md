```csharp
public static double Coefficient(IList<double> values)
{
    if (values == null) throw new ArgumentNullException(nameof(values));
    int n = values.Count;
    if (n <= 1) return 1.0;

    var sorted = values.ToArray();
    foreach (double v in sorted)
        if (!double.IsFinite(v) || v < 0.0 || v > 1.0)
            throw new ArgumentOutOfRangeException(nameof(values));

    Array.Sort(sorted);

    double sum = 0.0;
    foreach (double v in sorted) sum += v;
    double mean = sum / n;

    double sumSq = 0.0;
    foreach (double v in sorted) sumSq += (v - mean) * (v - mean);
    double sampleVariance = sumSq / (n - 1);

    double clamped = Math.Min(1.0, Math.Max(0.0, 4.0 * sampleVariance));
    return n * Math.Pow(1.0 - clamped, n);
}
```
