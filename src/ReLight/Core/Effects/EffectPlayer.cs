namespace ReLight.Core.Effects;

/// <summary>Plays one effect with given settings and keeps its own animation clock.</summary>
public sealed class EffectRunner(EffectInfo info)
{
    readonly Effect _effect = info.Create();
    double _time;

    public EffectInfo Info { get; } = info;

    public void Render(EffectFrame frame, EffectSettings settings, double seconds, Span<Rgb> output)
    {
        double step = seconds * EffectCatalog.TimeScale(settings.Speed);
        _time += step;
        frame.Time = _time;
        frame.Step = step;
        frame.Palette = Palette(settings);
        _effect.Render(frame, output);
    }

    static Rgb[] Palette(EffectSettings s) =>
        s.Colors.Select(c => Rgb.From(ColorMath.FromHex(c, System.Drawing.Color.Black))).ToArray();
}

/// <summary>
/// Rainbow mode for the strip: the chosen effect, or the playlist switching effects by itself,
/// with a soft cross-fade between them.
/// </summary>
public sealed class EffectPlayer(AppSettings settings)
{
    const double FadeSeconds = 1.5;

    EffectRunner? _current, _previous;
    double _fade = 1;
    double _playlistClock;
    Rgb[] _buffer = [];

    /// <summary>The effect editor shows its unsaved colors on the strip through this.</summary>
    public volatile EffectSettings? Preview;

    /// <summary>Effect on the strip right now (for the "now playing" mark in the window).</summary>
    public string? CurrentId => _current?.Info.Id;

    /// <param name="forcedEffect">An exclusion's own effect; it replaces the chosen one and the playlist.</param>
    public void Render(EffectFrame frame, double seconds, Span<Rgb> output, string? forcedEffect = null)
    {
        var target = forcedEffect != null && Preview == null && EffectCatalog.All.Any(e => e.Id == forcedEffect)
            ? forcedEffect
            : ChooseEffect(seconds);
        if (_current?.Info.Id != target)
        {
            _previous = _current;
            _current = new EffectRunner(EffectCatalog.Get(target));
            _fade = _previous == null ? 1 : 0;
        }

        var preview = Preview;
        _current!.Render(frame, preview != null && preview.Id == target ? preview : settings.EffectFor(target), seconds, output);

        if (_fade < 1 && _previous != null)
        {
            if (_buffer.Length != frame.Count) _buffer = new Rgb[frame.Count];
            _previous.Render(frame, settings.EffectFor(_previous.Info.Id), seconds, _buffer);
            _fade = Math.Min(1, _fade + seconds / FadeSeconds);
            float k = EffectMath.Smooth((float)_fade);
            for (int i = 0; i < frame.Count; i++) output[i] = Rgb.Lerp(_buffer[i], output[i], k);
            if (_fade >= 1) _previous = null;
        }
    }

    string ChooseEffect(double seconds)
    {
        if (Preview is { } p) return p.Id;
        var list = settings.PlaylistOn ? settings.Playlist.Where(id => EffectCatalog.All.Any(e => e.Id == id)).ToList() : [];
        if (list.Count == 0) return EffectCatalog.Get(settings.Effect).Id;

        int index = _current == null ? -1 : list.IndexOf(_current.Info.Id);
        if (index < 0)
        {
            _playlistClock = 0;
            return list[0];
        }
        _playlistClock += seconds;
        if (_playlistClock < Math.Max(5, settings.PlaylistSeconds)) return list[index];
        _playlistClock = 0;
        return list[(index + 1) % list.Count];
    }
}
