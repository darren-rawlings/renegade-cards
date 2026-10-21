using System;
using Godot;
using Renegade.Audio;
using Renegade.Core.Synth;

namespace Renegade.Cards;

/// <summary>
/// Card sounds, synthesised (no recordings): the flick of a card in a riffle, the slap of a card
/// landing on the felt, the swish of one flying, and the knock of a squared-up deck. Each has a few
/// variations so a riffle doesn't sound like a machine gun.
/// </summary>
public sealed class CardSounds
{
	private static readonly ImpactRecipe Flick = new(2600, new[] { 1.0, 1.73 }, new[] { 0.05, 0.03 }, 0.003, 1.0, 0.005, 0.85, 0.03);
	private static readonly ImpactRecipe Slap = new(170, new[] { 1.0, 2.4, 4.1 }, new[] { 0.22, 0.1, 0.05 }, 0.018, 1.0, 0.016, 0.5, 0.1);
	private static readonly ImpactRecipe Swish = new(400, new[] { 1.0 }, new[] { 0.0 }, 0.01, 1.0, 0.05, 0.22, 0.16);
	private static readonly ImpactRecipe Knock = new(140, new[] { 1.0, 2.1 }, new[] { 0.35, 0.12 }, 0.03, 0.7, 0.012, 0.35, 0.12);

	private readonly AudioStreamWav[] _flicks, _slaps, _swishes, _knocks;
	private readonly SoundPool _pool;
	private readonly Random _random = new();

	public CardSounds(SoundPool pool)
	{
		_pool = pool;
		_flicks = Make(Flick, 6);
		_slaps = Make(Slap, 4);
		_swishes = Make(Swish, 3);
		_knocks = Make(Knock, 2);
	}

	private static AudioStreamWav[] Make(ImpactRecipe recipe, int count)
	{
		var streams = new AudioStreamWav[count];
		for (int i = 0; i < count; i++)
			streams[i] = ImpactBank<int>.ToStream(ImpactSynth.Impact(recipe, 100 + i));
		return streams;
	}

	private AudioStreamWav Pick(AudioStreamWav[] set) => set[_random.Next(set.Length)];
	private float Pitch(float spread) => 1f + ((float)_random.NextDouble() - 0.5f) * spread;

	public void PlayFlick(Vector3 at) => _pool.Play(Pick(_flicks), at, -10f, Pitch(0.3f));
	public void PlaySlap(Vector3 at) => _pool.Play(Pick(_slaps), at, -4f, Pitch(0.15f));
	public void PlaySwish(Vector3 at) => _pool.Play(Pick(_swishes), at, -12f, Pitch(0.2f));
	public void PlayKnock(Vector3 at) => _pool.Play(Pick(_knocks), at, -2f, Pitch(0.1f));
}
