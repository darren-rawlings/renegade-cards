using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Renegade.Ads;
using Renegade.Audio;
using Renegade.Core;
using Renegade.Dev;
using Renegade.Play;
using Renegade.Store;
using Renegade.Ui;

namespace Renegade.Cards;

/// <summary>
/// What every Renegade card game shares, so each game is only its rules and how they're shown:
/// the table (2 to 5 seats, or a solitaire layout), the card looks and sounds, the dealer, glass
/// hands and fans at each seat, speech bubbles, the HUD (Style for card backs and fronts, Demo,
/// Send, Start over, Settings with the shop), ads every few games, the winner's fire, the
/// developer flags and checks, and a safe way to run the game's animations as tasks.
/// A game derives from this, fills in the abstract members and starts its rounds in
/// <see cref="NewRound"/>.
/// </summary>
public abstract partial class CardGame : Node3D
{
	public static readonly Color Muted = new("d9b99b");
	public static readonly Color Bright = new("fff3e0");
	public static readonly Color GlassTint = new(0.8f, 0.92f, 1f);
	public static readonly string[] Names = { "You", "Rosie", "Max", "Lena", "Theo" };

	protected Random Random { get; private set; } = new();
	protected readonly Scoreboard Wins = new();
	protected GameSettings Settings = null!;
	protected CardTable Table = null!;
	protected CardLooks Looks = null!;
	protected CardSounds Sounds = null!;
	protected Dealer Dealer = null!;
	protected GameHud Hud = null!;
	protected IAdService Ads = null!;
	protected readonly CardFan[] Fans = new CardFan[CardTable.MaxSeats];
	protected readonly SeatHands[] Hands = new SeatHands[CardTable.MaxSeats];

	private CardStyleMenu _styleMenu = null!;
	private AdSchedule? _adSchedule;
	private TableTalk? _talk;
	private int _talkToken;
	private Flames? _fire;
	private string? _art;
	private bool _handCheck;
	private bool _begin;
	private int _seatView = -1;
	private string? _clip;
	private bool _clipping;
	private CanvasLayer? _clipCover;
	private bool _shuffleLab, _quitAfterShuffle;
	private ShuffleStyle? _shuffleStyle;

	/// <summary>True while the table is acting something out; taps are ignored.</summary>
	protected bool Busy = true;

	/// <summary>Counts rounds, so an animation from an abandoned round can tell it should stop.</summary>
	protected int Round;

	/// <summary>The computer plays every seat (the --demo flag, used for recordings and checks).</summary>
	protected bool Demo;

	protected bool QuitAfterDemo;

	/// <summary>How many sit at the table (from Settings › Players, or the game's only size).</summary>
	protected int Players;

	// ------------------------------------------------------------------ What a game provides

	protected abstract string Title { get; }
	protected abstract string Hint { get; }
	protected abstract IReadOnlyList<TutorialPage> TutorialPages { get; }

	/// <summary>The table sizes the game allows; if they differ, Settings gets a Players choice.</summary>
	protected virtual int MinPlayers => 2;
	protected virtual int MaxPlayers => 2;
	protected virtual int DefaultPlayers => MinPlayers;

	/// <summary>Whether the game has a computer opponent with levels (solitaire games don't).</summary>
	protected virtual bool HasLevels => true;

	/// <summary>Whether players hold their cards in fans with glass hands (solitaire games don't).</summary>
	protected virtual bool HoldsCards => true;

	/// <summary>A solitaire layout: the camera looks more steeply down on a wider felt, with no seats.</summary>
	protected virtual bool Solitaire => false;

	/// <summary>How wide a solitaire layout is, in table units (Spider's ten piles want more than Klondike's seven).</summary>
	protected virtual float LayoutWidth => 5.3f;

	/// <summary>Whether your fan sorts itself by rank (games that arrange it themselves, like gin, say no).</summary>
	protected virtual bool SortYourHand => true;

	/// <summary>Whether players hold their cards in the left hand (War's piles need only the right).</summary>
	protected virtual bool HoldsFan => true;

	protected virtual string NewGameText => "Deal again";

	/// <summary>Adds the game's own rows to Settings (use <see cref="GameHud.AddToggle"/> and AddChoice).</summary>
	protected virtual void AddSettings() { }

	/// <summary>
	/// Sets the table round the players once it and its camera are up, before anyone's hands are
	/// made: by eye (<see cref="CardTable.SetByEye"/>), just above the status line. A game that needs
	/// room on the table for something of its own (Cribbage's board) does it its own way.
	/// </summary>
	protected virtual void LayOut(CardTable table) => table.SetByEye(Players, StatusTop);

	/// <summary>How far down the screen the status line's top is (GameHud's: 250 up from the bottom).</summary>
	protected float StatusTop => GetViewport().GetVisibleRect().Size.Y - 250;

	/// <summary>A game's own developer flag (after the shared ones).</summary>
	protected virtual void DevArg(string name, string value) { }

	/// <summary>Starts a new round: clear the table, shuffle, deal and play.</summary>
	protected abstract Task NewRound();

	/// <summary>True once a round has begun and isn't over (Start over then asks first).</summary>
	protected abstract bool InProgress { get; }

	/// <summary>A tap on the table, when nothing is being acted out: a ray from the camera.</summary>
	protected abstract void Tapped(Vector3 origin, Vector3 direction, Vector2 screen);

	/// <summary>The level changed in the Level menu (games rebuild their computer players).</summary>
	protected virtual void LevelChanged(Difficulty difficulty) { }

	// ------------------------------------------------------------------ Start-up

	public override void _Ready()
	{
		DevLog.Tag = Title.Length >= 3 ? Title[..3].ToLowerInvariant() : "crd";
		AudioWatch.Attach(this, Title);
		Settings = GameSettings.Load();
		Players = Math.Clamp(Settings.Extra.TryGetValue("players", out var n) && int.TryParse(n, out int p) ? p : DefaultPlayers, MinPlayers, MaxPlayers);
		ReadDeveloperArgs();
		if (_art != null)
		{
			_ = ArtCheck.Run(this, _art);
			return;
		}
		if (_handCheck)
		{
			Run(ShowHands());
			return;
		}
		if (_shuffleLab)
		{
			Run(ShuffleLab.Run(this, _shuffleStyle, _quitAfterShuffle));
			return;
		}
		AudioServer.SetBusMute(0, Settings.Muted);
		Run(Start());
	}

	private async Task ShowHands() => HandCheck.Build(this, await CardLooks.Create(this, CardFront.Classic, CardBack.Crimson));

	private async Task Start()
	{
		Table = new CardTable { Seats = Solitaire ? 1 : Players, Solitaire = Solitaire, SolitaireWidth = LayoutWidth };
		AddChild(Table);
		if (!Solitaire)
			LayOut(Table);
		var pool = new SoundPool();
		AddChild(pool);
		Sounds = new CardSounds(pool);
		Dealer = new Dealer(this, Sounds, Random) { Eye = Table.Camera.GlobalPosition };
		Looks = await CardLooks.Create(this, Saved("front", CardFront.Classic), Saved("back", CardBack.Crimson));

		if (HoldsCards && !Solitaire)
		{
			for (int s = 0; s < Players; s++)
			{
				Fans[s] = s == 0
					? new CardFan(Table.FanPivot[0], sorted: SortYourHand)
					: new CardFan(Table.FanPivot[s], sorted: false) { Radius = 0.5f, Spread = 9f, MaxArc = 80f };
				Hands[s] = new SeatHands(this, Sounds, Table.FanPivot[s], GlassTint, Table.HandRest[s], HoldsFan, Table.HolderRest[s], yours: s == 0);
				// Out of cards, the holding hand goes back down to its place.
				var hands = Hands[s];
				Fans[s].Emptied += () => hands.Hold(false);
			}
		}
		Table.SetCloth(Saved("cloth", TableCloth.Green));
		ApplySpeed();

		_adSchedule ??= new AdSchedule(Settings.GamesSinceAd);
		var theme = HudTheme.Warm;
		Ads = AdServices.Create(this, AdUnits, Demo, new PlaceholderAds { TextColor = theme.Text });
		Hud = new GameHud(theme);
		AddChild(Hud);
		Hud.Build(new HudOptions(Title, Hint, Modes: false, LevelOnly: HasLevels, NewGameText: NewGameText,
			FreezeSetting: false, CustomStyle: () => _styleMenu.Open()), Settings);
		// The tutorial holds play: the table pauses while it's open (the HUD keeps running).
		Hud.ProcessMode = ProcessModeEnum.Always;
		Hud.TutorialOpened += () => GetTree().Paused = true;
		Hud.TutorialClosed += () => GetTree().Paused = false;
		// The log of what's said at the table (not in solitaire: there's no one to talk to, and it would
		// be in the way of the cards).
		if (!Solitaire)
		{
			_talk = new TableTalk { KeepRounds = TalkRounds };
			Hud.AddChild(_talk);
		}
		Hud.NewGamePressed += OnNewGamePressed;
		Hud.RestartPressed += OnRestartPressed;
		Hud.SetTutorial(TutorialPages);
		Hud.ModeChosen += (mode, difficulty) =>
		{
			Settings.Difficulty = difficulty;
			Settings.Save();
			LevelChanged(difficulty);
		};
		var send = Hud.AddButton("Send", false, Control.LayoutPreset.TopLeft, 244, 344, 148, 196, 22);
		send.Pressed += () => Hud.Dialog("Send to a friend",
			$"Once {Title} is on Google Play, this opens your phone's share menu with a link to the game, so a friend can get it and play you.",
			("OK", true, null));
		if (MaxPlayers > MinPlayers)
		{
			var sizes = Enumerable.Range(MinPlayers, MaxPlayers - MinPlayers + 1).Select(x => x.ToString()).ToArray();
			Hud.AddChoice("Players", sizes, Players - MinPlayers, choice =>
			{
				Settings.Extra["players"] = (choice + MinPlayers).ToString();
				Settings.Save();
				GetTree().ReloadCurrentScene();
			});
		}
		var speeds = new[] { 1, 2, 3 };
		Hud.AddChoice("Speed", new[] { "Normal", "2×", "3×" }, System.Array.IndexOf(speeds, Speed) is int at and >= 0 ? at : 0, choice =>
		{
			Settings.Extra["speed"] = speeds[choice].ToString();
			Settings.Save();
			ApplySpeed();
		});
		AddSettings();

		_styleMenu = new CardStyleMenu(theme, Looks.Back, Looks.Front, Saved("cloth", TableCloth.Green));
		_styleMenu.BackChosen += back => { _ = Looks.SetBack(this, back); Save("back", back); };
		_styleMenu.FrontChosen += front => { _ = Looks.SetFront(this, front); Save("front", front); };
		_styleMenu.ClothChosen += cloth => { Table.SetCloth(cloth); Save("cloth", cloth); };
		Hud.AddChild(_styleMenu);
		if (!Demo)
			Hud.OfferTutorialOnce(Settings);
		if (_clip != null)
			BeginClip();
		Hud.ApplyDevFlags();
		if (_seatView >= 0 && _seatView < Players)
			LookAtSeat(_seatView);
		// Nothing is dealt until you say: you can change the settings (the number of players, say) first
		// without it costing a game. The demo just starts.
		if (Demo || _begin)
			await NewRound();
		else
			OfferBegin();
	}

	// ------------------------------------------------------------------ Tutorial clips

	/// <summary>How fast the game plays while a clip waits for its moment.</summary>
	private const float ClipFastForward = 12f;

	/// <summary>The colour the screen is covered in until the moment (the recorder cuts those frames away).</summary>
	public static readonly Color ClipCoverColour = new(1, 0, 1);

	/// <summary>
	/// `--clip=name` (tools/Record-Clips.ps1 records it): the computer plays every seat at high speed
	/// under a magenta cover, with the HUD hidden, until the game reaches the clip's moment
	/// (<see cref="ClipMoment"/>); then it plays at normal speed for the clip's length and quits.
	/// </summary>
	private void BeginClip()
	{
		Hud.Visible = false;
		_clipCover = new CanvasLayer { Layer = 100 };
		var cover = new ColorRect { Color = ClipCoverColour, MouseFilter = Control.MouseFilterEnum.Ignore };
		cover.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_clipCover.AddChild(cover);
		AddChild(_clipCover);
		_clipping = true;
		ApplySpeed();
		// A clip that never reaches its moment stops in the end.
		GetTree().CreateTimer(240, processAlways: true, ignoreTimeScale: true).Timeout += () => GetTree().Quit();
	}

	/// <summary>
	/// The game has reached a moment a tutorial clip shows (a battle, the count, the dummy going
	/// down). If it's the clip being recorded, the cover lifts, play goes back to normal speed, and
	/// the game quits after <paramref name="seconds"/>.
	/// </summary>
	/// <summary>The clip being recorded (`--clip=name`), if any.</summary>
	protected string? ClipName => _clip;

	protected void ClipMoment(string name, float seconds = 6)
	{
		if (!_clipping || name != _clip)
			return;
		_clipping = false;
		_clipCover?.QueueFree();
		ApplySpeed();
		GetTree().CreateTimer(seconds, processAlways: true, ignoreTimeScale: true).Timeout += () => GetTree().Quit();
	}

	/// <summary>`--seat-view=N`: the camera close in on a seat's fan, from your side of it, to check how its hands hold it.</summary>
	private void LookAtSeat(int seat)
	{
		var fan = Table.FanPivot[seat];
		var target = fan * new Vector3(0, 0.35f, 0);
		var from = target + (Table.Camera.GlobalPosition - target).Normalized() * 2.6f;
		Table.Camera.GlobalPosition = from;
		Table.Camera.LookAt(target, Vector3.Up);
	}

	/// <summary>The Begin game button, in the middle of the empty table, until it's pressed.</summary>
	private void OfferBegin()
	{
		var begin = Hud.AddButton("Begin game", true, Control.LayoutPreset.Center, -200, 200, -48, 48, 38);
		Hud.SetStatus("Choose your settings, then begin.", Muted);
		begin.Pressed += () =>
		{
			begin.QueueFree();
			Hud.SetStatus("", Muted);
			Run(NewRound());
		};
	}

	/// <summary>Google's test ad units until each game's real ones go in (see the release playbook).</summary>
	protected virtual AdMobConfig AdUnits => AdMobConfig.Test;

	// ------------------------------------------------------------------ Helpers for games

	/// <summary>Runs a task without waiting for it, but reports anything that goes wrong (otherwise it would vanish).</summary>
	protected static async void Run(Task task)
	{
		try
		{
			await task;
		}
		catch (Exception error)
		{
			GD.PrintErr($"Card game: {error}");
			DevLog.Write($"error: {error.Message}");
		}
	}

	protected async Task Pause(float seconds) => await ToSignal(GetTree().CreateTimer(seconds, processAlways: false), SceneTreeTimer.SignalName.Timeout);

	protected async Task Await(Tween? tween)
	{
		if (tween != null && tween.IsValid() && tween.IsRunning())
			await ToSignal(tween, Tween.SignalName.Finished);
	}

	protected T Saved<T>(string key, T fallback) where T : struct, Enum =>
		Settings.Extra.TryGetValue(key, out var text) && Enum.TryParse<T>(text, out var value) ? value : fallback;

	protected void Save<T>(string key, T value) where T : struct, Enum
	{
		Settings.Extra[key] = value.ToString();
		Settings.Save();
	}

	/// <summary>
	/// A match in progress (games won by each side, say), kept between launches: one for each number
	/// of players. Zeros if none has been saved (and in the demo, which plays its own and keeps none).
	/// </summary>
	protected int[] SavedMatch(int sides)
	{
		var tally = new int[sides];
		if (!Demo && Settings.Extra.TryGetValue($"match{Players}", out var text))
		{
			var parts = text.Split(',');
			for (int i = 0; i < sides && i < parts.Length; i++)
				int.TryParse(parts[i], out tally[i]);
		}
		return tally;
	}

	protected void SaveMatch(IEnumerable<int> tally)
	{
		if (Demo)
			return;
		Settings.Extra[$"match{Players}"] = string.Join(",", tally);
		Settings.Save();
	}

	protected bool Flag(string key, bool fallback = false) => Settings.Extra.TryGetValue(key, out var on) ? on == "on" : fallback;

	protected void SetFlag(string key, bool on)
	{
		Settings.Extra[key] = on ? "on" : "off";
		Settings.Save();
	}


	/// <summary>
	/// A HUD button that costs a rewarded ad, labelled "Undo (ad)" and so on: pressing it plays the
	/// ad, and <paramref name="rewarded"/> runs once it's been watched. With ads removed in the shop,
	/// the "(ad)" leaves the label and the button just works.
	/// </summary>
	protected Button AddAdButton(string text, Control.LayoutPreset preset, float left, float right, float top, float bottom, Action rewarded, Func<bool>? can = null)
	{
		string label = $"{text} (ad)";
		var button = Hud.AddButton(ShopRows.AdText(label), false, preset, left, right, top, bottom, 22);
		void Relabel() => button.Text = ShopRows.AdText(label);
		if (Shop.Current is { } shop)
		{
			shop.Changed += Relabel;
			button.TreeExiting += () => shop.Changed -= Relabel;
		}
		button.Pressed += () =>
		{
			// No ad for nothing: only when there's something to do.
			if (Busy || can?.Invoke() == false)
				return;
			DevLog.Write($"ad: rewarded for {text}");
			Ads.ShowRewarded(rewarded: rewarded, closed: () => { });
		};
		return button;
	}
	// ------------------------------------------------------------------ Talk at the table

	/// <summary>Deep blue: the hands of whoever is speaking.</summary>
	public static readonly Color SpeakingTint = new(0.25f, 0.42f, 1f);

	/// <summary>Deep yellow: the hands of whoever is being spoken to.</summary>
	public static readonly Color AskedTint = new(1f, 0.78f, 0.12f);

	/// <summary>How many rounds of talk the dialogue log keeps (two hands, by default).</summary>
	protected virtual int TalkRounds => 2;

	/// <summary>A player's name as the log shows it ("You" for seat 0).</summary>
	protected virtual string NameOf(int seat) => seat == 0 ? "You" : Names[seat];

	/// <summary>
	/// Something a player says, into the dialogue log under their name in their colour. Give
	/// <paramref name="to"/> and that player's hands turn deep yellow for a moment, while the hands of
	/// whoever's turn it is stay deep blue (see <see cref="ShowTurn"/>): who's asking whom, at a glance. Null text does nothing (kept for older calls).
	/// </summary>
	protected void Say(int seat, string? text, int? to = null)
	{
		if (text == null || _talk == null)
			return;
		_talk.Add(NameOf(seat), TableTalk.SeatColour(seat), text, Round);
		DevLog.Write($"say: {NameOf(seat)}{(to is { } t ? " to " + NameOf(t) : "")}: {text}");
		if (to is not { } asked)
			return;
		// Whoever is being spoken to turns yellow for a few seconds (whoever's turn it is stays blue).
		_asked = asked;
		Tints();
		int token = ++_talkToken;
		GetTree().CreateTimer(3.5f).Timeout += () =>
		{
			if (token == _talkToken)
			{
				_asked = -1;
				Tints();
			}
		};
	}

	private int _turn = -1, _asked = -1;

	/// <summary>
	/// Whose turn it is: their hands glow deep blue from now until the turn passes, so everyone can
	/// see who is about to play (-1 for nobody).
	/// </summary>
	protected void ShowTurn(int seat)
	{
		_turn = seat;
		Tints();
	}

	private void Tints()
	{
		for (int s = 0; s < CardTable.MaxSeats; s++)
			if (Hands[s] != null)
				Hands[s].Tint = s == _asked ? AskedTint : s == _turn ? SpeakingTint : GlassTint;
	}

	/// <summary>The table's own news in the log (who took a trick, a card turned): no name, in italics.</summary>
	protected void News(string text)
	{
		_talk?.Add(null, Muted, text, Round, news: true);
		DevLog.Write($"news: {text}");
	}


	/// <summary>
	/// A finished trick in the log, every card in the order played and who took it ("Rosie 2♣ ·
	/// Max 9♣ · Lena K♣ · You 4♣ → Lena"), so any trick can be checked afterwards.
	/// </summary>
	protected void TrickNews(IReadOnlyList<Play> trick, int winner, string? extra = null) =>
		News($"{string.Join(" · ", trick.Select(p => $"{NameOf(p.Seat)} {p.Card}"))} → {NameOf(winner)}{(extra != null ? ", " + extra : "")}");
	/// <summary>Clears the yellow of whoever was being spoken to (the turn's blue stays; the log keeps what was said).</summary>
	protected void SayNone()
	{
		_talkToken++;
		_asked = -1;
		Tints();
	}

	// ------------------------------------------------------------------ Speed

	/// <summary>The chosen play speed (Settings › Speed): 1, 2 or 3 times as fast.</summary>
	protected int Speed => Settings.Extra.TryGetValue("speed", out var s) && int.TryParse(s, out int n) && n is 1 or 2 or 3 ? n : 1;

	private float _boost = 1;

	/// <summary>An extra speed-up the game asks for on top of Settings (Old Maid when only the computers are left).</summary>
	protected float SpeedBoost
	{
		get => _boost;
		set
		{
			_boost = value;
			ApplySpeed();
		}
	}

	private void ApplySpeed() => Engine.TimeScale = _clipping ? ClipFastForward : Speed * _boost;

	/// <summary>"You" or a computer player's name.</summary>
	protected static string Who(int seat) => seat == 0 ? "you" : Names[seat];

	/// <summary>Removes every card and puts the hands back, ready for a new deal.</summary>
	protected void ClearTable()
	{
		_fire?.Extinguish();
		_fire = null;
		SayNone();
		_turn = -1;
		Tints();
		_talk?.Prune(Round);
		for (int s = 0; s < CardTable.MaxSeats; s++)
		{
			Hands[s]?.Reset();
			Fans[s]?.Clear();
		}
		foreach (var child in GetChildren())
			if (child is Card3D or WinCascade)
				child.QueueFree();
		Hud.SetNewGameVisible(false);
		Hud.SetRestartVisible(false);
	}

	/// <summary>
	/// Lays a new deck face down in the middle of the table and shuffles it on screen (riffles
	/// and a bridge). Returns the card objects in the order given (index 0 is the top card).
	/// </summary>
	protected async Task<List<Card3D>> ShuffleOnTable(IReadOnlyList<Card> order, bool animate = true)
	{
		var stack = new List<Card3D>();
		for (int i = 0; i < order.Count; i++)
		{
			var node = new Card3D(order[order.Count - 1 - i], Looks);
			AddChild(node);
			stack.Add(node);
		}
		Dealer.Stack(stack, Table.Centre);
		if (animate)
		{
			Hud.SetStatus("Shuffling…", Muted);
			await Pause(0.3f);
			// A different way of shuffling each deal.
			var style = ChosenShuffle ?? (ShuffleStyle)Random.Next(Enum.GetValues<ShuffleStyle>().Length);
			DevLog.Write($"shuffle: {style}");
			await Shuffler().Play(style, stack, Table.Centre);
		}
		// The shuffle moved the card objects around; give them the order wanted, top first.
		var top = new List<Card3D>(order.Count);
		for (int i = 0; i < order.Count; i++)
		{
			var node = stack[order.Count - 1 - i];
			node.Card = order[i];
			top.Add(node);
		}
		return top;
	}

	/// <summary>A shuffle forced for checks (--deal-shuffle=Name), or null: a different one each deal (players never choose).</summary>
	protected ShuffleStyle? ChosenShuffle { get; private set; }

	private Shuffles? _shuffles;

	/// <summary>
	/// The shuffles, done with your own hands (resting where they rest at your seat); a game where
	/// you hold no cards borrows a pair that only appear for the shuffle.
	/// </summary>
	private Shuffles Shuffler()
	{
		if (_shuffles != null)
			return _shuffles;
		GlassHand Spare(bool right)
		{
			var hand = new GlassHand(right, GlassTint, HandPose.Rest);
			AddChild(hand);
			hand.WarmUp();
			return hand;
		}
		var seat = Hands[0];
		var left = seat?.Holder ?? Spare(false);
		var right = seat?.Worker ?? Spare(true);
		var leftRest = seat?.Holder != null ? seat.HolderRestingPose : Shuffles.PalmDownAt(Table.HolderRest[0] with { Y = CardTable.FeltY }, Vector3.Forward);
		var rightRest = seat != null ? seat.RestingPose : Shuffles.PalmDownAt(Table.HandRest[0] with { Y = CardTable.FeltY }, Vector3.Forward);
		_shuffles = new Shuffles(this, Sounds, Random, Table.Camera.GlobalPosition, left, right)
		{
			LeftRest = leftRest,
			RightRest = rightRest,
			HideWhenResting = seat == null,
			LeftOnlyForShuffle = seat != null && seat.Holder == null,
		};
		if (seat == null || seat.Holder == null)
			_shuffles.Rest(Table.Centre);
		return _shuffles;
	}

	/// <summary>Deals cards from the top of the stack to the seats' piles, one at a time round the table.</summary>
	protected async Task DealRound(IReadOnlyList<Card3D> cards, IReadOnlyList<int> seats, float spacing = 0)
	{
		if (spacing <= 0)
			spacing = 0.6f / Math.Max(2, Players) + 0.04f;
		Hud.SetStatus("Dealing…", Muted);
		var onPile = new int[CardTable.MaxSeats];
		Tween? last = null;
		for (int j = 0; j < cards.Count; j++)
			last = Dealer.Deal(cards[j], Table.Pile[seats[j]], onPile[seats[j]]++, delay: j * spacing);
		await Await(last);
	}

	/// <summary>A player's working hand picks cards up from wherever they are and puts them into their fan.</summary>
	protected async Task IntoFan(int seat, IReadOnlyList<Card3D> cards)
	{
		if (cards.Count == 0)
			return;
		Hands[seat].Hold(true);
		foreach (var node in cards)
		{
			foreach (var fan in Fans)
				fan?.Remove(node);
			Fans[seat].Add(node);
		}
		var targets = cards.Select(node => Fans[seat].Slot(Fans[seat].Cards.ToList().IndexOf(node), Fans[seat].Count)).ToList();
		await Hands[seat].Carry(cards, targets);
		await Await(Fans[seat].Settle());
	}

	/// <summary>Everyone picks up their dealt pile and fans it, all at once.</summary>
	protected async Task PickUpAll(Func<int, IEnumerable<Card3D>> pileOf)
	{
		var pickups = new List<Task>();
		for (int s = 0; s < Players; s++)
			pickups.Add(IntoFan(s, pileOf(s).OrderByDescending(n => n.GlobalPosition.Y).ToList()));
		await Task.WhenAll(pickups);
	}

	/// <summary>Takes a card out of a fan and plays it face up to a spot on the table, with a pinch.</summary>
	protected async Task PlayToTable(int seat, Card3D card, Transform3D target)
	{
		Fans[seat]?.Remove(card);
		Fans[seat]?.Settle();
		if (Hands[seat] != null)
			await Hands[seat].Carry(new[] { card }, new[] { target }, seconds: 0.36f, lift: 0.25f);
		else
			await Await(CardMotion.Fly(card, target, 0.36f, lift: 0.25f, landed: () => Sounds.PlaySlap(target.Origin)));
	}

	// ------------------------------------------------------------------ Trick-taking games

	/// <summary>Where a seat's card goes in the middle of the table when played to a trick.</summary>
	protected Vector3 TrickSpot(int seat)
	{
		var toward = Table.Pile[seat] - Table.Centre;
		toward.Y = 0;
		return Table.Centre + toward.Normalized() * 0.42f + Vector3.Up * (0.003f * (seat + 1));
	}

	/// <summary>Plays a card from a seat's fan into the middle, face up, turned towards its player.</summary>
	protected Task PlayToTrick(int seat, Card3D card)
	{
		var toward = Table.Pile[seat] - Table.Centre;
		float yaw = Mathf.Atan2(toward.X, toward.Z) + ((float)Random.NextDouble() - 0.5f) * 0.2f;
		return PlayToTable(seat, card, FaceUpAt(TrickSpot(seat), yaw));
	}

	/// <summary>
	/// The trick's winner's hand gathers its cards and puts them face down on their pile of won
	/// tricks (offset a little each trick, so the count shows). <paramref name="won"/> is how many
	/// tricks they had before this one.
	/// </summary>
	protected async Task SweepTrick(int winner, IReadOnlyList<Card3D> trick, int won)
	{
		var pile = Table.Winnings[winner] + new Vector3((won % 7) * 0.05f, 0, (won / 7) * 0.12f);
		var targets = trick.Select((_, i) => Dealer.InStack(pile, won * 4 + i, yaw: 0.25f * (won % 2))).ToList();
		await Hands[winner].Carry(trick.OrderByDescending(n => n.GlobalPosition.Y).ToList(), targets, seconds: 0.34f, lift: 0.18f);
	}

	/// <summary>Greys the cards in your fan you can't play now (or clears it, with null).</summary>
	protected void DimUnplayable(IEnumerable<Card>? playable)
	{
		var allowed = playable?.ToHashSet();
		foreach (var node in Fans[0].Cards)
			node.Dimmed = allowed != null && !allowed.Contains(node.Card);
	}

	/// <summary>Keeps your fan in trick-game order: by suit, high cards first.</summary>
	protected void ArrangeBySuit()
	{
		var bySuit = Tricks.Sorted(Fans[0].Cards.Select(n => n.Card));
		var lookup = Fans[0].Cards.ToDictionary(n => n.Card);
		Fans[0].Reorder(bySuit.Select(c => lookup[c]));
		Fans[0].Settle(0.18f);
	}

	/// <summary>A card lying face up on the felt at a point, turned a little.</summary>
	protected static Transform3D FaceUpAt(Vector3 at, float yaw = 0) => new(Card3D.FaceUp(yaw), at);

	/// <summary>A card lying face down on the felt at a point.</summary>
	protected static Transform3D FaceDownAt(Vector3 at, float yaw = 0) => new(Card3D.FaceDown(yaw), at);

	/// <summary>The card (of a set) under a tap: the nearest one hit.</summary>
	protected static Card3D? HitCard(IEnumerable<Card3D> cards, Vector3 origin, Vector3 direction)
	{
		Card3D? best = null;
		float bestDistance = float.MaxValue;
		foreach (var card in cards)
			if (card.Hit(origin, direction) is { } d && d < bestDistance - 0.0005f)
			{
				best = card;
				bestDistance = d;
			}
		return best;
	}

	/// <summary>
	/// Ends a round: the status line, the winner's cards set alight (if given), a win counted for
	/// you if you won, and Deal again offered (or, in the demo, the next round started).
	/// </summary>
	protected void Finish(string message, int winner, IEnumerable<Card3D>? winnersCards = null, Vector3? at = null, IEnumerable<int>? winningSeats = null)
	{
		DevLog.Write($"finish: {message}");
		Hud.SetStatus(message, Bright);
		if (winner >= 0)
		{
			Wins.Win(first: winner == 0);
			var meshes = (winnersCards ?? Enumerable.Empty<Card3D>()).SelectMany(n => n.Meshes).ToList();
			if (meshes.Count > 0)
				_fire = Flames.SetAlight(this, at ?? Table.Centre, meshes, radius: 0.5f, size: 1.2f, withLight: true);
			// The winners' hands clasp and cheer; everyone else's slip off the table.
			var cheering = (winningSeats ?? new[] { winner }).ToHashSet();
			for (int s = 0; s < Players; s++)
			{
				if (Hands[s] is not { } hands)
					continue;
				if (cheering.Contains(s))
					hands.Cheer();
				else
					hands.Leave();
			}
		}
		Hud.SetRestartVisible(false);
		Hud.SetNewGameVisible(true);
		Busy = true;
		if (Demo)
			Run(DemoNext());
	}

	private async Task DemoNext()
	{
		await Pause(3f);
		if (QuitAfterDemo)
			GetTree().Quit();
		else
			OnNewGamePressed();
	}

	// ------------------------------------------------------------------ Input and the round's buttons

	/// <summary>The level can only change between games: its button is locked while one is under way.</summary>
	public override void _Process(double delta) => Hud?.SetModesLocked(HasLevels && !Demo && InProgress);

	private bool _dragging;
	private Vector2 _dragFrom;

	/// <summary>
	/// A press on the table may start a drag (games that move cards by dragging say so, and return
	/// true when the press picked something up); otherwise it's a tap.
	/// </summary>
	protected virtual bool BeginDrag(Vector3 origin, Vector3 direction, Vector2 screen) => false;

	/// <summary>The finger (or mouse) moved while dragging.</summary>
	protected virtual void DragTo(Vector3 origin, Vector3 direction) { }

	/// <summary>Let go. <paramref name="moved"/> is false when it was really just a tap.</summary>
	protected virtual void EndDrag(Vector3 origin, Vector3 direction, bool moved) { }

	/// <summary>Where a ray from the camera meets a level plane (the felt, or a little above it).</summary>
	protected static Vector3 OnPlane(Vector3 origin, Vector3 direction, float y)
	{
		if (Mathf.Abs(direction.Y) < 1e-4f)
			return origin;
		return origin + direction * ((y - origin.Y) / direction.Y);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (_dragging)
		{
			if (@event is InputEventMouseMotion motion)
			{
				var (o, d) = Table.Ray(motion.Position);
				DragTo(o, d);
			}
			else if (@event is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } up)
			{
				_dragging = false;
				var (o, d) = Table.Ray(up.Position);
				EndDrag(o, d, up.Position.DistanceTo(_dragFrom) > 14);
			}
			return;
		}
		if (Busy || Hud == null || Hud.MenuOpen || _styleMenu.Visible)
			return;
		if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
			return;
		var (origin, direction) = Table.Ray(click.Position);
		if (BeginDrag(origin, direction, click.Position))
		{
			_dragging = true;
			_dragFrom = click.Position;
			return;
		}
		Tapped(origin, direction, click.Position);
	}

	protected void OnNewGamePressed()
	{
		bool adDue = _adSchedule!.GameFinished();
		Settings.GamesSinceAd = _adSchedule.GamesSinceAd;
		Settings.Save();
		if (adDue)
		{
			DevLog.Write("ad: between-games interstitial");
			Ads.ShowInterstitial(() => Run(StartRound()));
		}
		else
		{
			Run(StartRound());
		}
	}

	/// <summary>Bumps the round count (so old animations stop), clears up, and starts the next round.</summary>
	protected async Task StartRound()
	{
		Round++;
		Busy = true;
		ClearTable();
		await NewRound();
	}

	/// <summary>Start over mid-game: against the computer it's a resignation unless an ad is watched.</summary>
	private void OnRestartPressed()
	{
		if (!InProgress)
		{
			Run(StartRound());
			return;
		}
		Hud.AskRestart(HasLevels && !Demo, countLoss =>
		{
			if (countLoss)
				Wins.Win(first: false);
			Run(StartRound());
		}, then => Ads.ShowRewarded(rewarded: then, closed: () => { }));
	}

	/// <summary>
	/// Developer flags after `--` (or in user://dev_args.txt on a phone): `--log`, `--demo` (the
	/// computer plays every seat), `--quit-after-demo`, `--begin` (deals at once, with no Begin game
	/// button), `--players=4`, `--games-since-ad=4`, `--art=user://art` (paints every card design to
	/// PNGs and quits), `--hands` (the glass hands up close), `--seat-view=N` (the camera close in on
	/// a seat's fan), `--bake-hands` (saves the hand meshes into the kit and quits), plus the HUD's
	/// `--tutorial=N` and `--settings`, and the game's own.
	/// </summary>
	private void ReadDeveloperArgs()
	{
		foreach (var (name, value) in DevArgs.Read())
		{
			switch (name)
			{
				case "--log":
					DevLog.Enabled = true;
					break;
				case "--demo":
					Demo = true;
					Settings.Muted = false;
					break;
				case "--bake-hands":
					GD.Print($"Baked {HandShape.Bake()} hand meshes into {HandShape.Baked}");
					GetTree().Quit();
					break;
				case "--clip":
					_clip = value;
					Demo = true;
					Settings.Muted = true;
					break;
				case "--seat-view":
					_seatView = int.Parse(value);
					break;
				case "--begin":
					_begin = true;
					break;
				case "--quit-after-demo":
					QuitAfterDemo = true;
					break;
				case "--players":
					Players = Math.Clamp(int.Parse(value), MinPlayers, MaxPlayers);
					break;
				case "--games-since-ad":
					_adSchedule = new AdSchedule(int.Parse(value));
					break;
				case "--art":
					_art = value;
					break;
				case "--hands":
					_handCheck = true;
					break;
				case "--shuffles":
					_shuffleLab = true;
					break;
				case "--shuffle":
					_shuffleLab = true;
					_shuffleStyle = Enum.TryParse<ShuffleStyle>(value, out var style) ? style : null;
					break;
				case "--deal-shuffle":
					ChosenShuffle = Enum.TryParse<ShuffleStyle>(value, out var forced) ? forced : null;
					break;
				case "--quit-after-shuffle":
					_quitAfterShuffle = true;
					break;
				case "--seed":
					Random = new Random(int.Parse(value));
					break;
				default:
					DevArg(name, value);
					break;
			}
		}
	}
}
