using CookMate.Api.Services;

namespace CookMate.Api.Tests;

public class FaceMatchingServiceTests
{
    private const int Dim = 8;
    private readonly FaceMatchingService _svc = new(new FaceOptions());

    // Vector gốc theo "người" (seed) cộng nhiễu nhỏ: cùng seed thì giống nhau
    private static float[] Person(int seed, double noise = 0.05, int noiseSeed = 0)
    {
        var rnd = new Random(seed);
        var baseVec = Enumerable.Range(0, Dim).Select(_ => (float)(rnd.NextDouble() * 2 - 1)).ToArray();
        var nrnd = new Random(1000 + noiseSeed);
        return baseVec.Select(x => x + (float)((nrnd.NextDouble() * 2 - 1) * noise)).ToArray();
    }

    private static float[] Orthogonal(int index)
    {
        var v = new float[Dim];
        v[index] = 1;
        return v;
    }

    [Fact]
    public void Cung_nguoi_duoc_nhan()
    {
        var candidates = new List<FaceCandidate>
        {
            new(1, FaceMatchingService.L2Normalize(Orthogonal(0))),
            new(2, FaceMatchingService.L2Normalize(Orthogonal(1))),
        };
        var queries = Enumerable.Range(0, 3)
            .Select(i => FaceMatchingService.L2Normalize(Person(0, 0, i).Select((_, k) => k == 0 ? 1f : 0.02f * i).ToArray()))
            .ToList();

        Assert.Equal(1, _svc.FindMatch(queries, candidates));
    }

    [Fact]
    public void Nguoi_khac_bi_tu_choi()
    {
        var candidates = new List<FaceCandidate> { new(1, Orthogonal(0)), new(2, Orthogonal(1)) };
        var stranger = Enumerable.Range(0, 3).Select(_ => Orthogonal(5)).ToList();

        Assert.Null(_svc.FindMatch(stranger, candidates));
    }

    [Fact]
    public void Ba_vector_khong_cung_mot_nguoi_bi_tu_choi()
    {
        var candidates = new List<FaceCandidate> { new(1, Orthogonal(0)), new(2, Orthogonal(1)) };
        var queries = new List<float[]> { Orthogonal(0), Orthogonal(0), Orthogonal(1) };

        Assert.Null(_svc.FindMatch(queries, candidates));
    }

    [Fact]
    public void Diem_duoi_nguong_bi_tu_choi()
    {
        var candidates = new List<FaceCandidate> { new(1, Orthogonal(0)) };
        // cosine = 0.6 < 0.80
        var q = new float[Dim];
        q[0] = 0.6f;
        q[1] = 0.8f;

        Assert.Null(_svc.FindMatch([q, q, q], candidates));
    }

    [Fact]
    public void Hai_nguoi_qua_giong_nhau_bi_tu_choi_do_khoang_cach()
    {
        var a = new float[Dim];
        a[0] = 1;
        var b = new float[Dim];
        b[0] = 0.99f;
        b[1] = 0.14f; // rất gần a
        var candidates = new List<FaceCandidate>
        {
            new(1, FaceMatchingService.L2Normalize(a)),
            new(2, FaceMatchingService.L2Normalize(b)),
        };

        Assert.Null(_svc.FindMatch([a, a, a], candidates));
    }

    [Fact]
    public void Chi_co_mot_nguoi_thi_bo_dieu_kien_khoang_cach()
    {
        var candidates = new List<FaceCandidate> { new(7, Orthogonal(0)) };

        Assert.Equal(7, _svc.FindMatch([Orthogonal(0), Orthogonal(0), Orthogonal(0)], candidates));
    }

    [Fact]
    public void Mau_dang_ky_dong_nhat()
    {
        var a = FaceMatchingService.L2Normalize(Person(1, 0.02, 1));
        var b = FaceMatchingService.L2Normalize(Person(1, 0.02, 2));
        var c = FaceMatchingService.L2Normalize(Person(1, 0.02, 3));

        Assert.True(_svc.AreSamplesConsistent([a, b, c]));
    }

    [Fact]
    public void Mau_dang_ky_khong_dong_nhat()
    {
        Assert.False(_svc.AreSamplesConsistent([Orthogonal(0), Orthogonal(0), Orthogonal(3)]));
    }

    [Fact]
    public void Trung_mat_tai_khoan_khac()
    {
        var existing = new List<FaceCandidate> { new(2, Orthogonal(0)) };

        Assert.True(_svc.IsDuplicate(Orthogonal(0), existing));
        Assert.False(_svc.IsDuplicate(Orthogonal(4), existing));
    }

    [Fact]
    public void Vector_sai_chieu_bi_tu_choi()
    {
        var ok = Orthogonal(0);
        var wrong = new float[Dim + 1];
        wrong[0] = 1;

        Assert.Throws<FaceValidationException>(() => _svc.Validate([ok, ok, wrong], 3, 5, Dim));
        // Vector đầu quyết định chiều khi hệ thống chưa có ai
        Assert.Throws<FaceValidationException>(() => _svc.Validate([ok, wrong, ok], 3, 5, null));
        Assert.Equal(Dim, _svc.Validate([ok, ok, ok], 3, 5, null));
    }

    [Fact]
    public void Vector_nan_infinity_toan_khong_va_sai_so_luong_bi_tu_choi()
    {
        var ok = Orthogonal(0);
        var nan = Orthogonal(0);
        nan[1] = float.NaN;
        var inf = Orthogonal(0);
        inf[2] = float.PositiveInfinity;

        Assert.Throws<FaceValidationException>(() => _svc.Validate([ok, ok, nan], 3, 5, Dim));
        Assert.Throws<FaceValidationException>(() => _svc.Validate([ok, ok, inf], 3, 5, Dim));
        Assert.Throws<FaceValidationException>(() => _svc.Validate([ok, ok, new float[Dim]], 3, 5, Dim));
        Assert.Throws<FaceValidationException>(() => _svc.Validate([ok, ok], 3, 5, Dim));
        Assert.Throws<FaceValidationException>(() => _svc.Validate([ok, ok, ok, ok, ok, ok], 3, 5, Dim));
    }

    [Fact]
    public void Chuyen_float_byte_hai_chieu_giu_nguyen_gia_tri()
    {
        var v = new[] { 0.5f, -1.25f, 3.14159f, 0f };
        var bytes = FaceMatchingService.ToBytes(v);

        Assert.Equal(16, bytes.Length);
        Assert.Equal(v, FaceMatchingService.FromBytes(bytes));
        // float32 little-endian: 0.5f = 0x3F000000
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x3F }, bytes[..4]);
    }

    [Fact]
    public void Average_va_L2Normalize_cho_vector_do_dai_1()
    {
        var avg = FaceMatchingService.Average([Orthogonal(0), Orthogonal(1)]);
        var len = Math.Sqrt(avg.Sum(x => (double)x * x));

        Assert.Equal(1.0, len, 5);
        Assert.Equal(avg[0], avg[1], 5);
    }
}
