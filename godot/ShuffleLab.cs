using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Renegade.Audio;
using Renegade.Play;
using Renegade.Ui;

namespace Renegade.Cards;

/// <summary>
/// Developer showcase (--shuffles): a card table with a deck and the dealer's glass hands, and a
/// button for each way of shuffling (<see cref="ShuffleStyle"/>), so the styles can be compared side
/// by side before one goes into the games. Slow motion halves the speed. With --shuffle=Name it
/// plays that one style straight away; --quit-after-shuffle closes after it (for recordings).
/// </summary>
public partial class ShuffleLab : Node3D
{
	private CardTable _table = null!;
	private CardLooks _looks = null!;
	private Shuffles _shuffles = null!;
	private readonly List<Card3D> _deck = new();
	private Label _title = null!;
	private Label _about = null!;
	private bool _busy;
	private ShuffleStyle _last = ShuffleStyle.RiffleBridge;

	public ShuffleStyle? Start { get; init; }
	public bool QuitAfter { get; init; }

	public static async Task Run(Node3D host, ShuffleStyle? start, bool quitAfter)
	{
		var lab = new ShuffleLab { Start = start, QuitAfter = quitAfter };
		host.AddChild(lab);
		await lab.Build();
	}

	private Vector3 At => _table.Centre;

	private async Task Build()
	{
		_table = new CardTable { Seats = 2 };
		AddChild(_table);
		var pool = new SoundPool();
		AddChild(pool);
		var sounds = new CardSounds(pool);
		_looks = await CardLooks.Create(this, CardFront.Classic, CardBack.Crimson);
		var tint = new Color(0.8f, 0.92f, 1f);
		var left = new GlassHand(right: false, tint, HandPose.Rest);
		var right = new GlassHand(right: true, tint, HandPose.Rest);
		AddChild(left);
		AddChild(right);
		right.WarmUp();
		left.WarmUp();
		_shuffles = new Shuffles(this, sounds, new Random(), _table.Camera.GlobalPosition, left, right);
		_shuffles.Rest(At);
		foreach (var card in Deck.Shuffled(new Random()))
		{
			var node = new Card3D(card, _looks);
			AddChild(node);
			_deck.Add(node);
		}
		Dealer.Stack(_deck, At);
		BuildUi();
		if (Start is { } style)
			Play(style);
	}

	private void BuildUi()
	{
		var theme = HudTheme.Warm;
		var layer = new CanvasLayer();
		AddChild(layer);
		var top = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		Widgets.Anchor(top, Control.LayoutPreset.TopWide, left: 24, right: -24, top: 40, bottom: 330);
		top.AddThemeConstantOverride("separation", 8);
		layer.AddChild(top);
		top.AddChild(Widgets.Label("Shuffle Lab", 44, theme.Text));
		_title = Widgets.Label("Pick a shuffle below", 30, theme.Text);
		top.AddChild(_title);
		_about = Widgets.Body("Each button acts out a different way of shuffling with the same cards and hands. Watch them, then say which one the card games should use.", 22, theme.TextMuted);
		top.AddChild(_about);

		var grid = new GridContainer { Columns = 2 };
		grid.AddThemeConstantOverride("h_separation", 12);
		grid.AddThemeConstantOverride("v_separation", 10);
		Widgets.Anchor(grid, Control.LayoutPreset.BottomWide, left: 24, right: -24, top: -330, bottom: -30);
		layer.AddChild(grid);
		foreach (ShuffleStyle style in Enum.GetValues<ShuffleStyle>())
		{
			var button = Widgets.Button(Shuffles.Name(style), 22, theme.ButtonDark, theme);
			button.CustomMinimumSize = new Vector2(320, 62);
			button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			var choice = style;
			button.Pressed += () => Play(choice);
			grid.AddChild(button);
		}
		var again = Widgets.Button("Again", 22, theme.Accent, theme);
		again.CustomMinimumSize = new Vector2(320, 62);
		again.Pressed += () => Play(_last);
		grid.AddChild(again);
		var slow = new CheckButton { Text = "Slow motion" };
		slow.AddThemeFontSizeOverride("font_size", 22);
		slow.Toggled += on => Engine.TimeScale = on ? 0.5 : 1;
		grid.AddChild(slow);
	}

	private async void Play(ShuffleStyle style)
	{
		if (_busy)
			return;
		_busy = true;
		_last = style;
		_title.Text = Shuffles.Name(style);
		_about.Text = Shuffles.About(style);
		try
		{
			await _shuffles.Play(style, _deck, At);
		}
		catch (Exception error)
		{
			GD.PrintErr($"Shuffle Lab: {error}");
		}
		_busy = false;
		if (QuitAfter)
		{
			await ToSignal(GetTree().CreateTimer(0.6f), SceneTreeTimer.SignalName.Timeout);
			GetTree().Quit();
		}
	}
}
