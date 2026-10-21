using System;
using Godot;

namespace Renegade.Cards;

/// <summary>Moves cards (and hands) through the air: eased, with an arc, turning on the way.</summary>
public static class CardMotion
{
	/// <summary>
	/// Flies a node to a global transform. The start is taken when the flight begins (after any
	/// delay), so flights can be queued. Returns the tween; await its Finished signal.
	/// </summary>
	public static Tween Fly(Node3D node, Transform3D to, float seconds, float lift = 0.3f, float delay = 0f, Action? landed = null)
	{
		var tween = node.CreateTween();
		if (delay > 0)
			tween.TweenInterval(delay);
		Transform3D from = default;
		bool started = false;
		tween.TweenMethod(Callable.From<float>(t =>
		{
			if (!started)
			{
				from = node.GlobalTransform;
				started = true;
			}
			float e = t * t * (3 - 2 * t);
			var position = from.Origin.Lerp(to.Origin, e) + Vector3.Up * lift * 4 * e * (1 - e);
			var rotation = from.Basis.GetRotationQuaternion().Slerp(to.Basis.GetRotationQuaternion(), e);
			node.GlobalTransform = new Transform3D(new Basis(rotation), position);
		}), 0f, 1f, seconds);
		if (landed != null)
			tween.TweenCallback(Callable.From(landed));
		return tween;
	}

	/// <summary>Flies straight there, without an arc (sliding across the felt, settling into a fan).</summary>
	public static Tween Slide(Node3D node, Transform3D to, float seconds, float delay = 0f, Action? landed = null) =>
		Fly(node, to, seconds, 0f, delay, landed);
}
