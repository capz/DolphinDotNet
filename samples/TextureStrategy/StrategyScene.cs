using System;
using System.Threading.Tasks;
using Dolphin.Diagnostics;
using Dolphin.Graphics;
using Dolphin.Timing;

// Real bounded MEM1/GX residency. No frame waits on a missing source request.
internal sealed class StrategyScene : IDisposable
{
    private const int W = TextureSource.WorldWidth, H = TextureSource.WorldHeight, Group = 4;
    private readonly GraphicsDevice device = GraphicsDevice.Default;
    private readonly Renderer renderer = new Renderer();
    private readonly Camera camera = new Camera(55, 4f / 3f, .1f, 100);
    private readonly Mesh unitQuad, atlasQuad;
    private readonly Mesh[] coarsePatches = new Mesh[(W / Group) * (H / Group)];
    private readonly Transform[] coarseTransforms = new Transform[(W / Group) * (H / Group)];
    private readonly bool[] visiblePatches = new bool[(W / Group) * (H / Group)];
    private readonly Texture2D coarse;
    private readonly Material coarseMaterial;
    private readonly Texture2D?[] detail = new Texture2D?[48];
    private readonly Material[] materials = new Material[48];
    private readonly Transform[] transforms = new Transform[48];
    private readonly ResidencySet resident = new ResidencySet(false);
    private readonly int[] wanted = new int[63], weights = new int[63], retryAfter = new int[W * H * 2], latency = new int[256];
    private readonly bool[] failedOnce = new bool[W * H * 2];
    private readonly float[] bindingMatrix = new float[16];
    private readonly byte[] pagePixels = new byte[4096], atlasPixels = new byte[65536];
    private readonly StreamTelemetry telemetry = new StreamTelemetry();
    private Task? request;
    private int requestId, requestFrame, requestGeneration, generation, credit, wantedCount;
    private bool requestAtlas, atlas, stopped;
    internal bool InjectFailures;
    internal int Frame, Promotions, Evictions, Failures, Cancelled, Switches, Reversals;
    internal int RequestedBytes, UploadedBytes, MaxBytes, Fallbacks, Detailed, Draws;
    private int maxLatency, uploadUs, maxUploadUs, budgetWaits, pendingFrames, lastDirection, frameUs, modeDisposals, staleBindings;
    private readonly int nativeBaseline;
    private float centerX = 8, centerY = 12;
    internal StrategyScene(bool startAtlas)
    {
        DebugOverlay.WriteLine("STREAMING: COARSE WORLD");
        coarse = Texture2D.Create(W, H, TextureSource.Coarse(), false);
        coarseMaterial = NewMaterial(coarse);
        unitQuad = Mesh.Create(Quad(1, 1, 0, 0, 1, 1));
        atlasQuad = Mesh.Create(Quad(Group, Group, 0, 0, 1, 1));
        for (int y = 0; y < H / Group; y++) for (int x = 0; x < W / Group; x++)
        {
            int id = y * (W / Group) + x;
            coarsePatches[id] = Mesh.Create(Quad(Group, Group,
                x * Group / (float)W, y * Group / (float)H, Group / (float)W, Group / (float)H));
            coarseTransforms[id] = new Transform { LocalPosition = new Vector3(x * Group, y * Group, 0) };
        }
        for (int i = 0; i < detail.Length; i++)
        {
            materials[i] = NewMaterial(null);
            transforms[i] = new Transform();
        }
        atlas = startAtlas; resident.Reset(atlas);
        nativeBaseline = device.NativeBytes;
        DebugOverlay.WriteLine("A: MODE  Y: CAMERA  X: FAULTS  B: EXIT");
        DebugOverlay.WriteLine("192 KIB DETAIL / 4 KIB PER FRAME / COARSE FALLBACK");
    }
    private static Material NewMaterial(Texture2D? texture) =>
        new Material { Texture = texture, Cull = CullMode.None, Lighting = LightingMode.Unlit };
    private static VertexPositionColorTexture V(float x, float y, float u, float v) =>
        new VertexPositionColorTexture(new Vector3(x, y, 0), Color32.White, new Vector2(u, v));
    private static VertexPositionColorTexture[] Quad(int width, int height, float u, float v, float du, float dv)
    {
        var vertices = new VertexPositionColorTexture[width * height * 6];
        int i = 0;
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            float u0 = u + x * du / width, u1 = u + (x + 1) * du / width;
            float v0 = v + y * dv / height, v1 = v + (y + 1) * dv / height;
            vertices[i++] = V(x, y, u0, v0); vertices[i++] = V(x + 1, y, u1, v0);
            vertices[i++] = V(x + 1, y + 1, u1, v1); vertices[i++] = V(x, y, u0, v0);
            vertices[i++] = V(x + 1, y + 1, u1, v1); vertices[i++] = V(x, y + 1, u0, v1);
        }
        return vertices;
    }
    private void Release(int slot, bool eviction)
    {
        var old = detail[slot];
        if (old == null) return;
        materials[slot].Texture = null; detail[slot] = null;
        // Between frames; EndFrame has completed prior GX reads.
        old.Dispose(); resident.Remove(slot);
        if (eviction) Evictions++; else modeDisposals++;
    }
    internal void ToggleMode()
    {
        generation++;
        for (int i = 0; i < detail.Length; i++) Release(i, false);
        atlas = !atlas; resident.Reset(atlas); credit = 0; Switches++;
    }
    internal void Move(bool automatic, float dx, float dy)
    {
        if (automatic)
        {
            int step = Frame % 192, direction = step < 96 ? 1 : -1;
            if (lastDirection != 0 && lastDirection != direction) Reversals++;
            lastDirection = direction;
            centerX = step < 96 ? 8 + step * .25f : 32 - (step - 96) * .25f;
            centerY = 12 + ((Frame / 192) % 2) * 8;
        }
        else { centerX += dx; centerY += dy; }
        if (centerX < 4) centerX = 4; if (centerX > W - 4) centerX = W - 4;
        if (centerY < 4) centerY = 4; if (centerY > H - 4) centerY = H - 4;
        camera.LookAt(new Vector3(centerX, centerY, 5.5f),
            new Vector3(centerX, centerY, 0), new Vector3(0, 1, 0));
    }
    private void BuildWanted()
    {
        wantedCount = 0;
        for (int i = 0; i < visiblePatches.Length; i++) visiblePatches[i] = false;
        // Center first, then outward rings. Visible coverage includes a guard cell.
        for (int ring = 0; ring <= 4; ring++)
            for (int dy = -3; dy <= 3; dy++) for (int dx = -4; dx <= 4; dx++)
            {
                int ax = dx < 0 ? -dx : dx, ay = dy < 0 ? -dy : dy;
                if ((ax > ay ? ax : ay) != ring) continue;
                int x = (int)centerX + dx, y = (int)centerY + dy;
                if (x < 0 || x >= W || y < 0 || y >= H) continue;
                int patch = (y / Group) * (W / Group) + x / Group;
                visiblePatches[patch] = true;
                int id = atlas ? patch : y * W + x;
                int found = -1;
                for (int i = 0; i < wantedCount; i++) if (wanted[i] == id) { found = i; break; }
                if (found >= 0) weights[found]++;
                else { wanted[wantedCount] = id; weights[wantedCount++] = 1; }
            }
        for (int i = 0; i < wantedCount; i++)
        {
            int slot = resident.Find(wanted[i]);
            if (slot >= 0) resident.Touched[slot] = Frame;
        }
    }
    private void Pump()
    {
        if (stopped) return;
        credit += 4096; if (credit > 65536) credit = 65536;
        if (request != null)
        {
            pendingFrames++;
            if (!request.IsCompleted) return;
            // Observe every task, even after a mode switch or camera cancellation.
            if (requestGeneration != generation || !ResidencySet.Contains(wanted, wantedCount, requestId))
            {
                try { request.GetAwaiter().GetResult(); } catch (Exception) { Failures++; }
                request = null; Cancelled++; return;
            }
            int bytes = requestAtlas ? 65536 : 4096;
            if (Frame - requestFrame < 4 || credit < bytes) { budgetWaits++; return; }
            try { request.GetAwaiter().GetResult(); }
            catch (Exception)
            {
                retryAfter[requestId + (requestAtlas ? W * H : 0)] = Frame + 20;
                Failures++; request = null; return;
            }
            request = null;
            if (!TextureSource.Matches(requestAtlas ? atlasPixels : pagePixels, requestId, requestAtlas))
                throw new InvalidOperationException("Source pixels do not match their requested world ID.");
            int victim = resident.Victim(wanted, wantedCount);
            if (victim < 0) { Cancelled++; return; }
            Release(victim, true);
            long started = Clock.Timestamp;
            var texture = Texture2D.Create(requestAtlas ? 128 : 32, requestAtlas ? 128 : 32,
                requestAtlas ? atlasPixels : pagePixels, false);
            if (texture.NativeBytes != resident.SlotBytes)
                throw new InvalidOperationException("Unexpected native texture allocation size.");
            uploadUs = (int)(Clock.Timestamp - started);
            if (uploadUs > maxUploadUs) maxUploadUs = uploadUs;
            detail[victim] = texture;
            resident.Commit(victim, requestId, Frame);
            int x = requestAtlas ? (requestId % (W / Group)) * Group : requestId % W;
            int y = requestAtlas ? (requestId / (W / Group)) * Group : requestId / W;
            transforms[victim].LocalPosition = new Vector3(x, y, .01f);
            transforms[victim].CopyWorldMatrix(bindingMatrix);
            if (bindingMatrix[12] != x || bindingMatrix[13] != y) staleBindings++;
            // Publish a complete spatial/material binding with the new content.
            // A slot's previous cached transform is never submitted for a new ID.
            transforms[victim] = new Transform { LocalPosition = new Vector3(x, y, .01f) };
            materials[victim] = NewMaterial(texture);
            credit -= bytes; UploadedBytes += bytes; Promotions++;
            int delay = Frame - requestFrame;
            if (delay > maxLatency) maxLatency = delay;
            latency[delay < 255 ? delay : 255]++;
            if (resident.Bytes > MaxBytes) MaxBytes = resident.Bytes;
        }
        if (resident.Victim(wanted, wantedCount) < 0) return;
        for (int i = 0; i < wantedCount; i++)
        {
            int id = wanted[i], key = id + (atlas ? W * H : 0);
            if (resident.Find(id) >= 0 || retryAfter[key] > Frame) continue;
            bool fail = InjectFailures && !failedOnce[key] && (Promotions + Failures) % 11 == 2;
            if (fail) failedOnce[key] = true;
            byte[] pixels = atlas ? atlasPixels : pagePixels;
            bool layout = atlas;
            requestId = id; requestAtlas = atlas; requestFrame = Frame; requestGeneration = generation;
            RequestedBytes += resident.SlotBytes;
            request = Task.Run(() => TextureSource.Fill(pixels, id, layout, fail));
            return;
        }
    }
    private int Percentile(int percent)
    {
        int target = (Promotions * percent + 99) / 100, count = 0;
        for (int i = 0; i < latency.Length; i++) { count += latency[i]; if (count >= target) return i; }
        return 255;
    }
    internal void Report()
    {
        DebugOverlay.WriteLine(atlas ? "ATLAS STREAMING: 3 X 64 KIB" : "PAGE STREAMING: 48 X 4 KIB");
        DebugOverlay.WriteLine("RESIDENT KIB " + (resident.Bytes / 1024) + " / 192");
        DebugOverlay.WriteLine("PROMOTIONS " + Promotions + " EVICTIONS " + Evictions);
        DebugOverlay.WriteLine("COARSE CELLS " + Fallbacks + " DETAIL CELLS " + Detailed);
        DebugOverlay.WriteLine("REQUEST KIB " + (RequestedBytes / 1024) + " UPLOAD KIB " + (UploadedBytes / 1024));
        DebugOverlay.WriteLine("FAILURES " + Failures + " CANCELLED " + Cancelled + " SWITCHES " + Switches);
        DebugOverlay.WriteLine("LATENCY P95 " + Percentile(95) + " P99 " + Percentile(99) + " FRAMES");
    }
    internal void Present()
    {
        long frameStart = Clock.Timestamp;
        BuildWanted(); Pump();
        if (device.NativeBytes != nativeBaseline + resident.Bytes || resident.Bytes > ResidencySet.Budget)
            throw new InvalidOperationException("Native detail ownership or byte budget mismatch.");
        Fallbacks = Detailed = Draws = 0;
        device.BeginFrame(new Color32(8, 10, 19));
        renderer.Begin3D(camera);
        for (int i = 0; i < coarsePatches.Length; i++) if (visiblePatches[i])
        {
            renderer.DrawMesh(coarsePatches[i], coarseMaterial, coarseTransforms[i]); Draws++;
        }
        for (int i = 0; i < wantedCount; i++)
        {
            int slot = resident.Find(wanted[i]);
            if (slot < 0) Fallbacks += weights[i];
            else
            {
                int id = wanted[i];
                int x = atlas ? (id % (W / Group)) * Group : id % W;
                int y = atlas ? (id / (W / Group)) * Group : id / W;
                transforms[slot].CopyWorldMatrix(bindingMatrix);
                if (bindingMatrix[12] != x || bindingMatrix[13] != y || materials[slot].Texture != detail[slot])
                    throw new InvalidOperationException("Resident content has a stale spatial/material binding.");
                renderer.DrawMesh(atlas ? atlasQuad : unitQuad, materials[slot], transforms[slot]);
                Detailed += weights[i]; Draws++;
            }
        }
        renderer.End3D();
        int[] t = telemetry.Values;
        t[0] = 0x5453; t[1] = 2; t[2] = Frame; t[3] = atlas ? 1 : 0;
        t[4] = (int)(centerX * 256); t[5] = (int)(centerY * 256);
        t[6] = resident.Bytes / 1024; t[7] = Promotions; t[8] = Evictions;
        t[9] = Fallbacks; t[10] = Detailed; t[11] = Failures; t[12] = Cancelled;
        t[13] = Switches; t[14] = Reversals; t[15] = MaxBytes / 1024;
        t[16] = RequestedBytes / 1024; t[17] = UploadedBytes / 1024;
        t[18] = maxLatency; t[19] = budgetWaits; t[20] = Fallbacks + Detailed;
        t[21] = device.NativeBytes / 1024; t[22] = staleBindings;
        t[23] = Draws; t[24] = uploadUs; t[25] = Percentile(95); t[26] = Percentile(99);
        t[27] = maxUploadUs;
        t[28] = frameUs / 100; t[29] = TextureSource.Phase;
        t[30] = modeDisposals; t[31] = coarse.NativeBytes / 1024;
        telemetry.Present();
        device.EndFrame(); frameUs = (int)(Clock.Timestamp - frameStart); Frame++;
    }
    internal bool Stop()
    {
        stopped = true;
        if (request != null)
        {
            if (!request.IsCompleted) return false;
            try { request.GetAwaiter().GetResult(); } catch (Exception) { }
            request = null;
        }
        return true;
    }
    public void Dispose()
    {
        if (!stopped && !Stop()) throw new InvalidOperationException("Drain the source request before disposal.");
        for (int i = 0; i < detail.Length; i++) Release(i, false);
        for (int i = 0; i < coarsePatches.Length; i++) coarsePatches[i].Dispose();
        coarse.Dispose(); unitQuad.Dispose(); atlasQuad.Dispose(); telemetry.Dispose();
    }
}
