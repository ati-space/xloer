using System.Diagnostics;

namespace FastShot;

/// <summary>Manual scrolling with a live stitched preview; auto-scroll is an optional helper.</summary>
public sealed class ScrollingCaptureSession
{
    readonly RecordRequest request;
    readonly IntPtr target, root;
    readonly Native.RECT targetBounds;
    readonly Action<BitmapSource, double> completed;
    readonly CancellationTokenSource cancellation = new();
    readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    PanelWindow? shade, controls, preview;
    ScrollingCaptureHud? hud;
    readonly ScrollingCapturePreview previewView = new();
    volatile bool automatic, finishRequested;
    bool running, discard, previewVisible = true, captureReady;
    Native.POINT autoPoint, previousCursor;
    int capturedHeight;
    string message = "Aşağı kaydırın; istediğiniz yerde Bitir’e basın.";
    public event Action? Finished;

    public ScrollingCaptureSession(RecordRequest request, Action<BitmapSource, double> completed)
    {
        this.request = request; this.completed = completed;
        var r = request.PixelRect;
        target = Native.WindowFromPoint(new Native.POINT { X = r.Left + r.Width / 2, Y = r.Top + r.Height / 2 });
        root = Native.GetAncestor(target, 2);
        Native.GetWindowRect(root, out targetBounds);
        capturedHeight = r.Height;
    }

    public void Show()
    {
        if (running || completion.Task.IsCompleted) return;
        running = true;
        try
        {
            var r = request.PixelRect; var monitor = request.Monitor.Bounds; var s = request.Scale;
            shade = new PanelWindow(new ScrollingCaptureShade(new Rect((r.Left - monitor.Left) / s, (r.Top - monitor.Top) / s, r.Width / s, r.Height / s)),
                monitor, transparent: true, activatable: false, clickThrough: true);
            shade.ExcludeFromCapture(); shade.Show(activate: false);
            hud = new ScrollingCaptureHud(ToggleAutomatic, Finish, Cancel, () => { previewVisible = !previewVisible; Refresh(); });
            controls = new PanelWindow(hud.Root, r, transparent: true, activatable: false);
            controls.ExcludeFromCapture();
            controls.Source.AddHook(Shortcut);
            Native.RegisterHotKey(controls.Handle, 1, Native.MOD_NOREPEAT, 0x0D);
            Native.RegisterHotKey(controls.Handle, 2, Native.MOD_NOREPEAT, 0x1B);
            controls.Closed += () => { if (running) Cancel(); };
            preview = new PanelWindow(previewView.Root, r, transparent: true, activatable: false, clickThrough: true);
            preview.ExcludeFromCapture();
            Refresh(); controls.Show(activate: false);
            _ = Run();
        }
        catch { Close(); throw; }
    }

    public void Cancel()
    {
        if (completion.Task.IsCompleted) return;
        discard = true; automatic = false; cancellation.Cancel();
        if (!running) Close();
    }
    public Task CancelAsync() { Cancel(); return completion.Task; }

    IntPtr Shortcut(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == Native.WM_HOTKEY)
        {
            handled = true;
            if (wParam.ToInt32() == 2) Cancel();
            else if (captureReady) Finish();
        }
        return IntPtr.Zero;
    }

    void Finish()
    {
        if (finishRequested || completion.Task.IsCompleted) return;
        automatic = false; RestoreCursor(); finishRequested = true; hud?.SetFinishing();
    }

    void ToggleAutomatic()
    {
        if (finishRequested) return;
        if (automatic) { automatic = false; RestoreCursor(); message = "Aşağı kaydırın; istediğiniz yerde Bitir’e basın."; Refresh(); return; }
        if (!TargetReady()) { message = "Hedef pencereye dönün; konumunu ve boyutunu değiştirmeyin."; Refresh(); return; }
        var r = request.PixelRect;
        var points = new[] { new Native.POINT { X = r.Left + r.Width / 2, Y = r.Top + r.Height / 2 },
            new Native.POINT { X = r.Left + 20, Y = r.Top + 20 }, new Native.POINT { X = r.Right - 20, Y = r.Top + 20 } };
        var available = points.Where(p => controls?.Bounds.Contains(p.X, p.Y) != true).ToArray();
        if (available.Length == 0) return;
        previousCursor = Native.CursorPosition(); autoPoint = available[0];
        Native.SetForegroundWindow(root);
        Native.SetCursorPos(autoPoint.X, autoPoint.Y); automatic = true;
        message = "Otomatik kaydırılıyor. Fareyi oynatarak durdurabilirsiniz."; Refresh();
    }

    bool TargetReady()
    {
        var foreground = Native.GetForegroundWindow();
        if (!Native.IsWindow(target) || Native.IsIconic(root) || (foreground != root && foreground != controls?.Handle)) return false;
        return Native.GetWindowRect(root, out var r) && r.Left == targetBounds.Left && r.Top == targetBounds.Top && r.Right == targetBounds.Right && r.Bottom == targetBounds.Bottom;
    }

    void RestoreCursor()
    {
        var cursor = Native.CursorPosition();
        if (cursor.X == autoPoint.X && cursor.Y == autoPoint.Y && (autoPoint.X != 0 || autoPoint.Y != 0))
            Native.SetCursorPos(previousCursor.X, previousCursor.Y);
        autoPoint = default;
    }

    async Task Run()
    {
        var token = cancellation.Token; var r = request.PixelRect;
        try
        {
            await Task.Delay(180, token);
            var result = await Task.Run(async () =>
            {
                using var capture = new GdiCapture(r.Width, r.Height);
                byte[]? Grab()
                {
                    if (!TargetReady()) return null;
                    if (!capture.Grab(r.Left, r.Top, cursor: false)) throw new IOException("Ekran okunamadı.");
                    return TargetReady() ? capture.ToArray() : null;
                }
                ScrollStitcher? stitcher = null;
                var wheelTime = Stopwatch.StartNew(); var unchanged = 0; string? warning = null;
                var lastMessage = ""; var idleInfo = "Aşağı kaydırın; istediğiniz yerde Bitir’e basın.";
                while (!token.IsCancellationRequested)
                {
                    var finish = finishRequested;
                    if (finish) await Task.Delay(160, token);
                    var frame = Grab();
                    BitmapSource? image = null;
                    var info = automatic ? "Otomatik kaydırılıyor. Fareyi oynatarak durdurabilirsiniz." : idleInfo;
                    if (frame is null)
                    {
                        automatic = false;
                        info = "Bekletildi: hedef pencereye dönün; konumunu ve boyutunu değiştirmeyin.";
                    }
                    else
                    {
                        var next = ScrollAppendResult.Added;
                        if (stitcher is null) stitcher = new ScrollStitcher(frame, r.Width, r.Height);
                        else next = stitcher.Append(frame, token);
                        if (next == ScrollAppendResult.Added)
                        { image = stitcher.Preview(300, 720); unchanged = 0; warning = null; idleInfo = "Aşağı kaydırın; istediğiniz yerde Bitir’e basın."; }
                        else if (next == ScrollAppendResult.Revisited)
                        { info = idleInfo = "Bu bölüm zaten yakalandı. Aşağı kaydırarak devam edebilirsiniz."; unchanged = 0; warning = null; }
                        else if (next == ScrollAppendResult.Ambiguous)
                        {
                            automatic = false;
                            info = idleInfo = warning = "Daha yavaş kaydırın veya son yakalanan bölüme dönün; önizleme korundu.";
                        }
                        else if (next == ScrollAppendResult.LimitReached)
                        {
                            automatic = false; info = idleInfo = warning = "Görüntü sınırına ulaşıldı. Bitir ile yakalanan bölümü alın.";
                        }
                        else if (automatic && wheelTime.ElapsedMilliseconds >= 600 && ++unchanged >= 3)
                        { automatic = false; info = idleInfo = "Sayfa sonu: Bitir’e basın veya elle kaydırmaya devam edin."; }
                    }
                    if (image is not null || info != lastMessage)
                    {
                        lastMessage = info;
                        var height = stitcher?.Height ?? r.Height; var ready = stitcher is not null;
                        await controls!.Content.Dispatcher.InvokeAsync(() =>
                        {
                            capturedHeight = height; message = info; captureReady = ready;
                            previewView.Update(image, r.Width, height, 440);
                            if (!finishRequested) Refresh();
                        });
                    }
                    if (finish) return (image: stitcher?.Image(), warning: frame is null ? "Son kare alınamadı. Son geçerli önizleme saklandı." : warning);
                    if (automatic && wheelTime.ElapsedMilliseconds >= 600)
                    {
                        var cursor = Native.CursorPosition();
                        if (cursor.X != autoPoint.X || cursor.Y != autoPoint.Y || !TargetReady()) automatic = false;
                        else if (!Native.ScrollWheel(-120)) { automatic = false; throw new InvalidOperationException("Windows kaydırma girdisini kabul etmedi."); }
                        wheelTime.Restart();
                    }
                    await Task.Delay(80, token);
                }
                return (image: (BitmapSource?)null, warning: (string?)null);
            }, token);
            if (!discard && result.image is not null)
            {
                shade?.Hide(); controls?.Hide(); preview?.Hide();
                completed(result.image, request.Scale);
                if (result.warning is { } warning) Toast.Show("Kaydırmalı yakalama", Icons.Scan, warning);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            RestoreCursor(); Close();
            if (!discard) Output.ShowError(error, "Kaydırmalı yakalama durdu");
        }
        finally { RestoreCursor(); Close(); }
    }

    void Refresh()
    {
        if (hud is null || controls is null || preview is null || completion.Task.IsCompleted) return;
        hud.Update(message, request.PixelRect.Width, capturedHeight, automatic, captureReady, previewVisible);
        var work = request.Monitor.Work; var s = controls.Scale;
        var bar = hud.Measure(new Size(work.Width / s, work.Height / s));
        var maxPreviewHeight = Math.Max(1, Math.Min(440, work.Height / s - bar.Height - 80));
        var thumb = previewView.Update(null, request.PixelRect.Width, capturedHeight, maxPreviewHeight);
        var r = request.PixelRect;
        var placement = ScrollingCaptureLayout.Place(new Rect(r.Left, r.Top, r.Width, r.Height), new Rect(work.Left, work.Top, work.Width, work.Height),
            new Size(Math.Ceiling(bar.Width * s), Math.Ceiling(bar.Height * s)), new Size(Math.Ceiling(thumb.Width * s), Math.Ceiling(thumb.Height * s)), 10 * s);
        static Native.RECT Bounds(Rect rect) => new((int)rect.Left, (int)rect.Top, (int)Math.Ceiling(rect.Right), (int)Math.Ceiling(rect.Bottom));
        controls.SetBounds(Bounds(placement.Toolbar));
        if (!placement.Preview.IsEmpty) preview.SetBounds(Bounds(placement.Preview));
        if (previewVisible && previewView.HasImage && !placement.Preview.IsEmpty) preview.Show(activate: false); else preview.Hide();
    }

    void Close()
    {
        if (completion.Task.IsCompleted) return;
        running = false;
        if (controls is not null && !controls.IsDisposed)
        {
            Native.UnregisterHotKey(controls.Handle, 1); Native.UnregisterHotKey(controls.Handle, 2);
            controls.Source.RemoveHook(Shortcut);
        }
        preview?.Dispose(); controls?.Dispose(); shade?.Dispose();
        cancellation.Dispose(); completion.TrySetResult(); Finished?.Invoke();
    }
}
