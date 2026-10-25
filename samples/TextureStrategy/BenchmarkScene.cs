using System;
using System.Threading.Tasks;
using Dolphin.Diagnostics;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Timing;

internal sealed class BenchmarkScene : IDisposable
{
    private readonly GraphicsDevice device = GraphicsDevice.Default;
    private readonly Renderer renderer = new Renderer();
    private readonly Camera camera = new Camera(55, 4f / 3f, .1f, 100);
    private readonly Texture2D coarse;
    private readonly Material coarseMaterial;
    private readonly Mesh[] coarseMeshes = new Mesh[96], quads = new Mesh[3];
    private readonly Transform[] coarseTransforms = new Transform[96], transforms = new Transform[96];
    private readonly Material[] materials = new Material[96];
    private readonly Texture2D?[] textures = new Texture2D?[96];
    private readonly byte[][] scratch = { new byte[4096], new byte[16384], new byte[65536] };
    private readonly BenchmarkModel model = new BenchmarkModel();
    private readonly BenchmarkResult[] results = new BenchmarkResult[9];
    private readonly StreamTelemetry telemetry = new StreamTelemetry();
    private readonly int nativeBaseline;
    private Task? request;
    private int requestId, requestGroup, nextCase = -1, currentCase, selected, reportSecond = -1;
    private long requestAt, caseAt, previousFrame;
    private bool stopped, autoAdvance = true, resetSuite, summary;
    private int completedMask, coverage, draws, sceneUs;
    private int previousCoverage, previousElapsed;
    private bool recordPrevious;
    private float centerX = 8, centerY = 12;
    internal int Frame, FramesAfterDone;
    internal bool Completed => completedMask == 511;
    private BenchmarkResult Current => results[currentCase];
    internal static int GroupFor(int index) => index % 3 == 0 ? 1 : index % 3 == 1 ? 2 : 4;
    internal static int BudgetFor(int index) => index / 3 == 0 ? 192 : index / 3 == 1 ? 256 : 384;
    private static string Label(int index) => index % 3 == 0 ? "PAGE" : index % 3 == 1 ? "A2X2" : "A4X4";
    internal BenchmarkScene()
    {
        DebugOverlay.PerformanceVisible = false;
        for (int i = 0; i < 9; i++) results[i] = new BenchmarkResult();
        coarse = Texture2D.Create(48, 32, TextureSource.Coarse(), false);
        coarseMaterial = MaterialFor(coarse);
        for (int i = 0; i < 3; i++) quads[i] = Mesh.Create(Quad(1 << i, 0, 0, 1, 1));
        for (int y = 0; y < 8; y++) for (int x = 0; x < 12; x++)
        {
            int id = y * 12 + x;
            coarseMeshes[id] = Mesh.Create(Quad(4, x / 12f, y / 8f, 1 / 12f, 1 / 8f));
            coarseTransforms[id] = new Transform { LocalPosition = new Vector3(x * 4, y * 4, 0) };
        }
        for (int i = 0; i < 96; i++) { materials[i] = MaterialFor(null); transforms[i] = new Transform(); }
        nativeBaseline = device.NativeBytes;
        BeginCase(0);
    }
    private static Material MaterialFor(Texture2D? texture) =>
        new Material { Texture = texture, Cull = CullMode.None, Lighting = LightingMode.Unlit };
    private static VertexPositionColorTexture V(float x, float y, float u, float v) =>
        new VertexPositionColorTexture(new Vector3(x, y, 0), Color32.White, new Vector2(u, v));
    private static VertexPositionColorTexture[] Quad(int group, float u, float v, float du, float dv)
    {
        var vertices = new VertexPositionColorTexture[group * group * 6];
        int at = 0;
        for (int y = 0; y < group; y++) for (int x = 0; x < group; x++)
        {
            float u0 = u + x * du / group, u1 = u + (x + 1) * du / group;
            float v0 = v + y * dv / group, v1 = v + (y + 1) * dv / group;
            vertices[at++] = V(x, y, u0, v0); vertices[at++] = V(x + 1, y, u1, v0);
            vertices[at++] = V(x + 1, y + 1, u1, v1); vertices[at++] = V(x, y, u0, v0);
            vertices[at++] = V(x + 1, y + 1, u1, v1); vertices[at++] = V(x, y + 1, u0, v1);
        }
        return vertices;
    }
    private void Release(int slot, bool eviction)
    {
        var texture = textures[slot]; if (texture == null) return;
        materials[slot].Texture = null; textures[slot] = null;
        texture.Dispose(); model.Remove(slot);
        if (eviction) Current.Evictions++;
    }
    private void BeginCase(int index)
    {
        for (int i = 0; i < 96; i++) Release(i, false);
        if (resetSuite)
        {
            for (int i = 0; i < 9; i++) results[i].Reset();
            completedMask = 0; resetSuite = false;
        }
        currentCase = selected = index; Current.Reset(); completedMask &= ~(1 << index);
        model.Configure(GroupFor(index), BudgetFor(index));
        nextCase = -1; summary = false; reportSecond = -1; FramesAfterDone = 0;
        centerX = 8; centerY = 12; caseAt = previousFrame = Clock.Timestamp;
        recordPrevious = false;
    }
    internal void Input(ControllerState pad)
    {
        if (pad.A.WasPressed) { autoAdvance = true; resetSuite = true; nextCase = 0; }
        if (pad.X.WasPressed) { autoAdvance = false; nextCase = (currentCase / 3) * 3 + (currentCase + 1) % 3; }
        if (pad.Y.WasPressed) { autoAdvance = false; nextCase = ((currentCase / 3 + 1) % 3) * 3 + currentCase % 3; }
        if (pad.DPadLeft.WasPressed) { selected = (selected + 8) % 9; summary = false; reportSecond = -1; }
        if (pad.DPadRight.WasPressed) { selected = (selected + 1) % 9; summary = false; reportSecond = -1; }
        if (pad.R.WasPressed) { summary = true; reportSecond = -1; }
    }
    private void ObserveDiscard()
    {
        if (request == null) return;
        // No fault injection in this mode: an unexpected source error must surface.
        request.GetAwaiter().GetResult(); request = null; Current.Cancelled++;
    }
    private void Pump()
    {
        if (request != null)
        {
            if (!request.IsCompleted) return;
            request.GetAwaiter().GetResult(); request = null;
            int slot = model.Victim();
            if (!model.Desired(requestId) || slot < 0) { Current.Cancelled++; return; }
            Release(slot, true);
            long at = Clock.Timestamp;
            int layout = currentCase % 3, size = requestGroup * 32;
            var texture = Texture2D.Create(size, size, scratch[layout], false);
            if (texture.NativeBytes != model.SlotBytes) throw new InvalidOperationException("Benchmark texture size mismatch.");
            int uploadUs = (int)(Clock.Timestamp - at);
            textures[slot] = texture; model.Commit(slot, requestId);
            materials[slot].Texture = texture;
            int x = (requestId % (48 / requestGroup)) * requestGroup;
            int y = (requestId / (48 / requestGroup)) * requestGroup;
            transforms[slot].LocalPosition = new Vector3(x, y, .01f);
            Current.UploadedKiB += model.SlotBytes / 1024;
            Current.Promoted((int)(Clock.Timestamp - requestAt), uploadUs);
            if (model.Bytes / 1024 > Current.PeakKiB) Current.PeakKiB = model.Bytes / 1024;
        }
        if (model.Victim() < 0) return;
        for (int i = 0; i < model.DesiredCount; i++)
        {
            int id = model.Wanted[i]; if (model.Find(id) >= 0) continue;
            int group = model.Group; byte[] pixels = scratch[currentCase % 3];
            requestId = id; requestGroup = group; requestAt = Clock.Timestamp;
            Current.RequestedKiB += model.SlotBytes / 1024;
            request = Task.Run(() => BenchmarkSource.Fill(pixels, id, group));
            return;
        }
    }
    internal void Present()
    {
        long now = Clock.Timestamp;
        if (!stopped && nextCase >= 0 && (request == null || request.IsCompleted))
        { ObserveDiscard(); BeginCase(nextCase); now = Clock.Timestamp; }
        int elapsed = (int)((now - caseAt) / 1000), frameUs = (int)(now - previousFrame);
        previousFrame = now;
        // Weight the image that was actually displayed during this interval.
        if (recordPrevious && !stopped && nextCase < 0 && !Current.Complete)
            Current.Record(previousElapsed, frameUs, previousCoverage);
        bool active = !stopped && nextCase < 0 && !Current.Complete;
        if (active && elapsed >= 8000)
        {
            Current.Complete = true; completedMask |= 1 << currentCase;
            active = false; reportSecond = -1;
            if (currentCase == 8 || !autoAdvance) summary = true;
        }
        if (active)
        {
            if (elapsed < 2000) { centerX = 8; centerY = 12; }
            else
            {
                int motion = elapsed - 2000;
                centerX = motion < 3000 ? 8 + motion * .008f : 32 - (motion - 3000) * .008f;
                centerY = motion < 3000 ? 12 : 20;
            }
            model.Build(centerX, centerY); Pump(); coverage = model.Coverage();
        }
        else if (request != null && request.IsCompleted) ObserveDiscard();
        if (!stopped && Current.Complete && autoAdvance && currentCase < 8 && elapsed >= 8500 && nextCase < 0)
            nextCase = currentCase + 1;
        if (Completed) FramesAfterDone++;
        recordPrevious = active; previousCoverage = coverage; previousElapsed = elapsed;
        if (device.NativeBytes != nativeBaseline + model.Bytes || model.Bytes > model.BudgetKiB * 1024)
            throw new InvalidOperationException("Benchmark native ownership or budget mismatch.");
        camera.LookAt(new Vector3(centerX, centerY, 5.5f), new Vector3(centerX, centerY, 0), new Vector3(0, 1, 0));
        long drawAt = Clock.Timestamp;
        device.BeginFrame(new Color32(8, 10, 19)); renderer.Begin3D(camera); draws = 0;
        for (int y = 0; y < 8; y++) for (int x = 0; x < 12; x++)
        {
            if (x * 4 + 4 <= centerX - BenchmarkModel.HalfWidth || x * 4 >= centerX + BenchmarkModel.HalfWidth ||
                y * 4 + 4 <= centerY - BenchmarkModel.HalfHeight || y * 4 >= centerY + BenchmarkModel.HalfHeight) continue;
            int id = y * 12 + x; renderer.DrawMesh(coarseMeshes[id], coarseMaterial, coarseTransforms[id]); draws++;
        }
        for (int i = 0; i < model.Capacity; i++) if (textures[i] != null && model.Area(model.Ids[i]) > 0)
        { renderer.DrawMesh(quads[currentCase % 3], materials[i], transforms[i]); draws++; }
        renderer.End3D(); int submitUs = (int)(Clock.Timestamp - drawAt);
        if (active && submitUs > Current.MaxSubmitUs) Current.MaxSubmitUs = submitUs;
        if (elapsed / 1000 != reportSecond)
        { Report(); reportSecond = elapsed / 1000; }
        int[] t = telemetry.Values;
        t[0] = 0x424d; t[1] = 1; t[2] = Frame; t[3] = currentCase;
        t[4] = (int)(centerX * 256); t[5] = (int)(centerY * 256);
        t[6] = model.Bytes / 1024; t[7] = Current.Promotions; t[8] = Current.Evictions;
        t[9] = coverage; t[10] = model.Group; t[11] = model.BudgetKiB; t[12] = elapsed;
        t[13] = completedMask; t[14] = (int)(Current.DurationUs / 1000); t[15] = Current.StaticCoverage;
        t[16] = Current.RequestedKiB; t[17] = Current.UploadedKiB; t[18] = Current.LatencyP95Ms;
        t[19] = Current.LongestBelow95Ms; t[20] = model.Bytes / 1024 - model.UsefulKiB();
        t[21] = device.NativeBytes / 1024; t[22] = Current.Cancelled; t[23] = draws;
        t[24] = Current.MaxUploadUs; t[25] = Current.FramePercentile(95) / 100;
        t[26] = Current.FramePercentile(99) / 100; t[27] = Current.MaxFrameUs / 100;
        t[28] = sceneUs / 100; t[29] = Current.MotionCoverage;
        t[30] = Current.TimeTo95Ms < 0 ? 65535 : Current.TimeTo95Ms; t[31] = Current.PeakKiB;
        telemetry.Present(); device.EndFrame(); sceneUs = (int)(Clock.Timestamp - drawAt); Frame++;
    }
    private static string Pct(int basisPoints) => (basisPoints / 100) + "." + ((basisPoints % 100) / 10);
    private static string Ms(int us) => (us / 1000) + "." + ((us % 1000) / 100);
    private static string N(int value, int width)
    {
        string s = value.ToString(); while (s.Length < width) s = " " + s; return s;
    }
    private void Report()
    {
        if (summary)
        {
            DebugOverlay.WriteLine("LAYOUT KIB STATIC MOVE PCT F99MS UPKIB A:RERUN START:STRESS");
            for (int i = 0; i < 9; i++)
            {
                var r = results[i];
                DebugOverlay.WriteLine(Label(i) + " " + BudgetFor(i) + (r.Complete ?
                    " " + N(r.StaticCoverage / 100, 3) + " " + N(r.MotionCoverage / 100, 3) +
                    " " + Ms(r.FramePercentile(99)) + " " + r.UploadedKiB : " PENDING"));
            }
            return;
        }
        var result = results[selected];
        DebugOverlay.WriteLine((selected == currentCase && !Current.Complete ? "BENCH " : "RESULT ") +
            (selected + 1) + "/9 " + Label(selected) + " " + BudgetFor(selected) + " KIB");
        DebugOverlay.WriteLine("2 SEC COLD / 6 SEC ROUTE - PROCEDURAL SOURCE");
        DebugOverlay.WriteLine((selected == currentCase && !Current.Complete ? "LIVE " + Pct(coverage) : "MEAN " + Pct(result.MeanCoverage)) +
            " STATIC " + Pct(result.StaticCoverage) + " MOVE " + Pct(result.MotionCoverage));
        DebugOverlay.WriteLine(selected == currentCase && !Current.Complete ?
            "RESIDENT " + model.Bytes / 1024 + " USEFUL " + model.UsefulKiB() + " KIB" : "PEAK RESIDENT KIB " + result.PeakKiB);
        DebugOverlay.WriteLine("FRAME P95 " + Ms(result.FramePercentile(95)) + " P99 " + Ms(result.FramePercentile(99)) + " MAX " + Ms(result.MaxFrameUs));
        DebugOverlay.WriteLine("REQUEST " + result.RequestedKiB + " UPLOAD " + result.UploadedKiB + " KIB EVICT " + result.Evictions);
        DebugOverlay.WriteLine("FILL95 MS " + result.TimeTo95Ms + " LOAD95 " + result.LatencyP95Ms + " MAX UP US " + result.MaxUploadUs);
        DebugOverlay.WriteLine("SUBMIT MAX US " + result.MaxSubmitUs + " BELOW95 MAX MS " + result.LongestBelow95Ms);
        DebugOverlay.WriteLine("A:SUITE X:LAYOUT Y:BUDGET LEFT/RIGHT:RESULT R:TABLE");
        DebugOverlay.WriteLine("START:STRESS B:EXIT - COUNTERS RESET FOR EACH CASE");
    }
    internal bool Stop()
    {
        stopped = true;
        if (request != null) { if (!request.IsCompleted) return false; ObserveDiscard(); }
        return true;
    }
    public void Dispose()
    {
        if (!Stop()) throw new InvalidOperationException("Drain benchmark work before disposal.");
        for (int i = 0; i < 96; i++) Release(i, false);
        for (int i = 0; i < 96; i++) coarseMeshes[i].Dispose();
        for (int i = 0; i < 3; i++) quads[i].Dispose();
        coarse.Dispose(); telemetry.Dispose();
    }
}
