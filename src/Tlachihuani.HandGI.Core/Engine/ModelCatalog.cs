// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
using System.Security.Cryptography;
using HandGI.Core.Util;

namespace HandGI.Core.Engine;

/// <summary>What kind of file a model is: an ONNX graph, or a MediaPipe task bundle (TFLite inside a zip).</summary>
public enum ModelKind { Onnx, MediaPipeTask }

/// <summary>One model HandGI needs, pinned by size and SHA-256.</summary>
public sealed record ModelFile(string Title, string FileName, long Bytes, string Sha256, string Url, string License, ModelKind Kind = ModelKind.Onnx);

public sealed record ModelStatus(ModelFile Model, string? Path, bool Verified, string Message);

/// <summary>
/// The two MediaPipe hand models (Apache-2.0, OpenCV Zoo ONNX exports) and Google's MediaPipe gesture
/// recognizer bundle (Apache-2.0), whose two small gesture networks HandGI rebuilds as GPU graphs.
/// They ship next to the executable; a copy downloaded on request goes to %LOCALAPPDATA%\HandGI\models.
/// Every load is verified against the pinned SHA-256, so a corrupted or swapped file is refused, never
/// "mostly works".
/// </summary>
public static class ModelCatalog
{
    public static readonly ModelFile Palm = new(
        "Palm detector · MediaPipe (OpenCV Zoo, 2023-02)",
        "palm_detection_mediapipe_2023feb.onnx",
        3_905_734,
        "78FF51C38496B7FC8B8EBDB6CC8C1ABB02FA6C38427C6848254CDABA57FCCE7C",
        "https://huggingface.co/opencv/palm_detection_mediapipe/resolve/main/palm_detection_mediapipe_2023feb.onnx",
        "Apache-2.0");

    public static readonly ModelFile Hand = new(
        "Hand landmarks · MediaPipe (OpenCV Zoo, 2023-02)",
        "handpose_estimation_mediapipe_2023feb.onnx",
        4_099_621,
        "DB0898AE717B76B075D9BF563AF315B29562E11F8DF5027A1EF07B02BEF6D81C",
        "https://huggingface.co/opencv/handpose_estimation_mediapipe/resolve/main/handpose_estimation_mediapipe_2023feb.onnx",
        "Apache-2.0");

    /// <summary>Google's gesture recognizer, version 1 (the same bytes Google serves as "latest", 2023-04-26).</summary>
    public static readonly ModelFile Gestures = new(
        "Gesture recognizer · MediaPipe (Google, float16, v1)",
        "gesture_recognizer.task",
        8_373_440,
        "97952348CF6A6A4915C2EA1496B4B37EBABC50CBBF80571435643C455F2B0482",
        "https://storage.googleapis.com/mediapipe-models/gesture_recognizer/gesture_recognizer/float16/1/gesture_recognizer.task",
        "Apache-2.0",
        ModelKind.MediaPipeTask);

    public static IReadOnlyList<ModelFile> All { get; } = new[] { Palm, Hand, Gestures };

    /// <summary>Loads, verifies and reads Google's gesture networks (throws a message a person can act on).</summary>
    public static global::HandGI.Core.Gestures.MediaPipeGestureModel LoadGestures() =>
        global::HandGI.Core.Gestures.MediaPipeGestureModel.FromTask(LoadVerified(Gestures));

    private static IEnumerable<string> Candidates(ModelFile model)
    {
        yield return Path.Combine(AppPaths.AppModelsDir, model.FileName);
        yield return Path.Combine(AppPaths.UserModelsDir, model.FileName);
    }

    public static ModelStatus Check(ModelFile model)
    {
        string? firstProblem = null;
        foreach (var path in Candidates(model))
        {
            if (!File.Exists(path)) continue;
            var (ok, message) = Verify(model, File.ReadAllBytes(path));
            if (ok) return new ModelStatus(model, path, true, $"Verified ({model.Bytes / 1048576.0:0.0} MB, SHA-256 OK)");
            firstProblem ??= $"{path}: {message}";
        }
        return new ModelStatus(model, null, false, firstProblem ?? "Not found.");
    }

    public static (bool Ok, string Message) Verify(ModelFile model, byte[] data)
    {
        if (data.LongLength != model.Bytes) return (false, $"size {data.LongLength:N0} bytes, expected {model.Bytes:N0}");
        string hash = Convert.ToHexString(SHA256.HashData(data));
        return string.Equals(hash, model.Sha256, StringComparison.OrdinalIgnoreCase)
            ? (true, "SHA-256 OK")
            : (false, $"SHA-256 {hash[..12]}... does not match the pinned {model.Sha256[..12]}...");
    }

    /// <summary>Reads and verifies a model; throws a message a person can act on when it is missing or wrong.</summary>
    public static byte[] LoadVerified(ModelFile model)
    {
        foreach (var path in Candidates(model))
        {
            if (!File.Exists(path)) continue;
            var data = File.ReadAllBytes(path);
            if (Verify(model, data).Ok) return data;
        }
        throw new FileNotFoundException(
            $"{model.Title} ({model.FileName}) is missing or damaged. Use Setup → Download models, or copy it into {AppPaths.AppModelsDir}.",
            model.FileName);
    }

    /// <summary>Downloads one model into the per-user model folder, verifying size and SHA-256 before it is used.</summary>
    public static async Task<string> DownloadAsync(ModelFile model, IProgress<(long Done, long Total)>? progress, CancellationToken cancellationToken)
    {
        string target = Path.Combine(AppPaths.UserModelsDir, model.FileName);
        string partial = target + ".partial";
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("HandGI/0.1");
        using (var response = await http.GetAsync(model.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? model.Bytes;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var file = File.Create(partial);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                done += read;
                progress?.Report((done, total));
            }
        }

        var (ok, message) = Verify(model, await File.ReadAllBytesAsync(partial, cancellationToken).ConfigureAwait(false));
        if (!ok)
        {
            File.Delete(partial);
            throw new InvalidDataException($"The downloaded {model.FileName} was rejected: {message}.");
        }
        File.Move(partial, target, overwrite: true);
        return target;
    }
}
