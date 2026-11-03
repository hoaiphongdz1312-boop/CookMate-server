using System.Buffers.Binary;

namespace CookMate.Api.Services;

// Ngưỡng nhận diện, đọc từ mục "Face" trong appsettings (KE_HOACH_v2 mục 3)
public class FaceOptions
{
    // Cosine tối thiểu để coi là cùng người khi đăng nhập
    public double MatchThreshold { get; set; } = 0.80;

    // Người giống nhất phải hơn người thứ hai ít nhất chừng này
    public double MatchMargin { get; set; } = 0.08;

    // Khi đăng ký: mọi cặp mẫu phải giống nhau ít nhất chừng này
    public double EnrollSampleMinSimilarity { get; set; } = 0.75;

    // Khi đăng ký: giống mặt của tài khoản khác từ mức này là trùng
    public double DuplicateThreshold { get; set; } = 0.75;
}

public class FaceValidationException(string message) : Exception(message);

public record FaceCandidate(int UserId, float[] Embedding);

// Toán vector khuôn mặt, không phụ thuộc DB để test riêng được.
// Không ghi embedding hay điểm cosine ra log.
public class FaceMatchingService(FaceOptions options)
{
    // Chặn request quá lớn; MobileFaceNet thường cho 128 hoặc 192 chiều
    public const int MaxDim = 2048;

    public FaceOptions Options => options;

    // Kiểm tra số lượng, số chiều, NaN/Infinity, vector toàn 0.
    // expectedDim = null khi hệ thống chưa có ai đăng ký mặt: lấy chiều của vector đầu tiên.
    // Trả về số chiều của các vector.
    public int Validate(IReadOnlyList<float[]>? vectors, int minCount, int maxCount, int? expectedDim)
    {
        if (vectors == null || vectors.Count < minCount || vectors.Count > maxCount)
        {
            throw new FaceValidationException(minCount == maxCount
                ? $"Cần đúng {minCount} vector khuôn mặt"
                : $"Cần từ {minCount} đến {maxCount} vector khuôn mặt");
        }

        var dim = expectedDim ?? vectors[0]?.Length ?? 0;
        if (dim is <= 0 or > MaxDim)
            throw new FaceValidationException("Số chiều vector khuôn mặt không hợp lệ");

        foreach (var v in vectors)
        {
            if (v == null || v.Length != dim)
                throw new FaceValidationException("Vector khuôn mặt sai số chiều");

            var sumSq = 0.0;
            foreach (var x in v)
            {
                if (!float.IsFinite(x))
                    throw new FaceValidationException("Vector khuôn mặt chứa giá trị không hợp lệ");
                sumSq += (double)x * x;
            }
            if (sumSq == 0)
                throw new FaceValidationException("Vector khuôn mặt không hợp lệ");
        }

        return dim;
    }

    public static float[] L2Normalize(float[] v)
    {
        var sumSq = 0.0;
        foreach (var x in v) sumSq += (double)x * x;
        var result = new float[v.Length];
        if (sumSq == 0) return result;
        var inv = 1 / Math.Sqrt(sumSq);
        for (var i = 0; i < v.Length; i++) result[i] = (float)(v[i] * inv);
        return result;
    }

    // Không giả định đầu vào đã chuẩn hóa. Vector toàn 0 cho 0.
    public static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Hai vector khác số chiều");
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * b[i];
            na += (double)a[i] * a[i];
            nb += (double)b[i] * b[i];
        }
        if (na == 0 || nb == 0) return 0;
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    // Chuẩn hóa từng mẫu, lấy trung bình rồi chuẩn hóa lại (mỗi mẫu đóng góp như nhau)
    public static float[] Average(IReadOnlyList<float[]> samples)
    {
        if (samples.Count == 0) throw new ArgumentException("Cần ít nhất 1 mẫu");
        var dim = samples[0].Length;
        var sum = new double[dim];
        foreach (var s in samples)
        {
            if (s.Length != dim) throw new ArgumentException("Các mẫu khác số chiều");
            var n = L2Normalize(s);
            for (var i = 0; i < dim; i++) sum[i] += n[i];
        }
        return L2Normalize(sum.Select(x => (float)x).ToArray());
    }

    // Mọi cặp mẫu đăng ký phải giống nhau đủ ngưỡng (cùng một người)
    public bool AreSamplesConsistent(IReadOnlyList<float[]> samples)
    {
        for (var i = 0; i < samples.Count; i++)
        for (var j = i + 1; j < samples.Count; j++)
        {
            if (Cosine(samples[i], samples[j]) < options.EnrollSampleMinSimilarity)
                return false;
        }
        return true;
    }

    // Mặt đăng ký có giống mặt của tài khoản khác không (danh sách đã loại chính người đăng ký)
    public bool IsDuplicate(float[] embedding, IEnumerable<FaceCandidate> others) =>
        others.Any(c => Cosine(embedding, c.Embedding) >= options.DuplicateThreshold);

    // Đăng nhập: trả về UserId khi và chỉ khi cả 3 vector cùng khớp một người,
    // điểm >= MatchThreshold và hơn người thứ hai >= MatchMargin
    // (bỏ điều kiện khoảng cách khi chỉ có 1 người đăng ký mặt). Không khớp trả null.
    public int? FindMatch(IReadOnlyList<float[]> queries, IReadOnlyList<FaceCandidate> candidates)
    {
        if (queries.Count == 0 || candidates.Count == 0) return null;

        int? matched = null;
        foreach (var q in queries)
        {
            var userId = BestFor(q, candidates);
            if (userId == null) return null;
            if (matched != null && matched != userId) return null;
            matched = userId;
        }
        return matched;
    }

    private int? BestFor(float[] query, IReadOnlyList<FaceCandidate> candidates)
    {
        FaceCandidate? best = null;
        var bestScore = double.NegativeInfinity;
        double? secondScore = null;

        foreach (var c in candidates)
        {
            if (c.Embedding.Length != query.Length) continue;
            var score = Cosine(query, c.Embedding);
            if (best == null || score > bestScore)
            {
                if (best != null) secondScore = bestScore;
                best = c;
                bestScore = score;
            }
            else if (secondScore == null || score > secondScore)
            {
                secondScore = score;
            }
        }

        if (best == null || bestScore < options.MatchThreshold) return null;
        if (secondScore != null && bestScore - secondScore < options.MatchMargin) return null;
        return best.UserId;
    }

    // Lưu DB dạng float32 little-endian, 4 byte mỗi phần tử
    public static byte[] ToBytes(float[] v)
    {
        var bytes = new byte[v.Length * sizeof(float)];
        for (var i = 0; i < v.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * sizeof(float)), v[i]);
        return bytes;
    }

    public static float[] FromBytes(byte[] bytes)
    {
        if (bytes.Length % sizeof(float) != 0)
            throw new ArgumentException("Độ dài dữ liệu khuôn mặt không hợp lệ");
        var v = new float[bytes.Length / sizeof(float)];
        for (var i = 0; i < v.Length; i++)
            v[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * sizeof(float)));
        return v;
    }
}
