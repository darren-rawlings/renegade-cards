using System.Collections.Generic;
using Godot;

namespace Renegade.Cards;

/// <summary>
/// The table's dialogue log: one panel just under the top buttons where everything said at the
/// table appears as "<b>Name</b>: text", each name in its player's colour. New lines slide in
/// from below and the log scrolls up smoothly; drag it to read back. Each line remembers the
/// round it was said in, and lines from more than <see cref="KeepRounds"/> rounds ago are
/// dropped. Nothing disappears while it's still being read.
/// </summary>
public partial class TableTalk : Control
{
	public const int VisibleLines = 3;
	private const int FontSize = 25, LineHeight = 34;

	private readonly ScrollContainer _scroll;
	private readonly VBoxContainer _lines;
	private readonly List<(RichTextLabel Label, int Round)> _said = new();
	private double _readingUntil;
	private Tween? _tween;

	/// <summary>How many rounds (hands, or games) of talk the log keeps.</summary>
	public int KeepRounds { get; set; } = 2;

	public TableTalk()
	{
		SetAnchorsPreset(LayoutPreset.TopWide);
		OffsetLeft = 20;
		OffsetRight = -20;
		OffsetTop = 206;
		OffsetBottom = 206 + VisibleLines * LineHeight + 14;
		MouseFilter = MouseFilterEnum.Ignore;

		var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
		panel.SetAnchorsPreset(LayoutPreset.FullRect);
		panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.07f, 0.045f, 0.03f, 0.62f),
			CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14,
			ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 6, ContentMarginBottom = 6,
		});
		AddChild(panel);

		_scroll = new ScrollContainer
		{
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever,
			MouseFilter = MouseFilterEnum.Pass,
			ScrollDeadzone = 8,
		};
		panel.AddChild(_scroll);
		_lines = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_lines.AddThemeConstantOverride("separation", 0);
		_scroll.AddChild(_lines);
		// While someone is reading back, new lines don't yank the log to the bottom.
		_scroll.GuiInput += e =>
		{
			if (e is InputEventMouseButton or InputEventScreenDrag or InputEventMouseMotion { ButtonMask: MouseButtonMask.Left })
				_readingUntil = Time.GetTicksMsec() / 1000.0 + 4;
		};
		Visible = false;
	}

	/// <summary>A player's colour: gold for you, then rose, sky, green and violet round the table.</summary>
	public static Color SeatColour(int seat) => seat switch
	{
		0 => new Color("f2c14e"),
		1 => new Color("f08a9c"),
		2 => new Color("7cc0f5"),
		3 => new Color("8fd694"),
		_ => new Color("c4a3f2"),
	};

	/// <summary>Adds a line: a name in its colour (or none, for the table's own news) and what was said.</summary>
	public void Add(string? name, Color colour, string text, int round, bool news = false)
	{
		Visible = true;
		var label = new RichTextLabel
		{
			BbcodeEnabled = true,
			FitContent = true,
			ScrollActive = false,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			MouseFilter = MouseFilterEnum.Pass,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		label.AddThemeFontSizeOverride("normal_font_size", FontSize);
		label.AddThemeFontSizeOverride("bold_font_size", FontSize);
		label.AddThemeColorOverride("default_color", news ? new Color("cdb59a") : new Color("fff3e0"));
		string body = Escape(text);
		label.Text = name == null ? (news ? $"[i]{body}[/i]" : body) : $"[b][color=#{colour.ToHtml(false)}]{Escape(name)}[/color][/b]: {body}";
		label.Modulate = new Color(1, 1, 1, 0);
		_lines.AddChild(label);
		_said.Add((label, round));
		Prune(round);
		label.CreateTween().TweenProperty(label, "modulate:a", 1f, 0.35f);
		// Let the new line take its height first.
		GetTree().CreateTimer(0.06).Timeout += Follow;
	}

	/// <summary>Drops lines from rounds before the ones kept.</summary>
	public void Prune(int round)
	{
		while (_said.Count > 0 && _said[0].Round <= round - KeepRounds)
		{
			_said[0].Label.QueueFree();
			_said.RemoveAt(0);
		}
	}

	public void Clear()
	{
		foreach (var (label, _) in _said)
			label.QueueFree();
		_said.Clear();
		Visible = false;
	}

	/// <summary>Scrolls smoothly to the newest line (unless the player is reading back).</summary>
	private void Follow()
	{
		if (Time.GetTicksMsec() / 1000.0 < _readingUntil)
			return;
		var bar = _scroll.GetVScrollBar();
		float target = (float)Mathf.Max(0, bar.MaxValue - bar.Page);
		_tween?.Kill();
		_tween = CreateTween();
		_tween.TweenMethod(Callable.From<float>(v => _scroll.ScrollVertical = (int)v), (float)_scroll.ScrollVertical, target, 0.45f)
			.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
	}

	private static string Escape(string text) => text.Replace("[", "[lb]");
}
