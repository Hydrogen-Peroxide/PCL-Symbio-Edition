// SPDX-License-Identifier: MPL-2.0
// Copyright (c) 2026 PCL Living Fork contributors.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
namespace PCLLF.Wpf.Motion;

/// <summary>Designer-facing spring parameters. Response is a natural period, not a fixed duration.</summary>
public readonly struct SpringParameters {
    public SpringParameters(double responseSeconds, double dampingRatio = 1,
        double positionTolerance = .0001, double velocityTolerance = .001) {
        RequirePositiveFinite(responseSeconds, nameof(responseSeconds));
        RequirePositiveFinite(dampingRatio, nameof(dampingRatio));
        RequirePositiveFinite(positionTolerance, nameof(positionTolerance));
        RequirePositiveFinite(velocityTolerance, nameof(velocityTolerance));
        if (!double.IsFinite(2 * Math.PI / responseSeconds))
            throw new ArgumentOutOfRangeException(nameof(responseSeconds));
        ResponseSeconds = responseSeconds;
        DampingRatio = dampingRatio;
        PositionTolerance = positionTolerance;
        VelocityTolerance = velocityTolerance;
    }

    public double ResponseSeconds { get; }
    public double DampingRatio { get; }
    public double PositionTolerance { get; }
    public double VelocityTolerance { get; }

    internal void Validate() {
        // A default(struct) is not a usable physical spring.
        RequirePositiveFinite(ResponseSeconds, nameof(ResponseSeconds));
        RequirePositiveFinite(DampingRatio, nameof(DampingRatio));
        RequirePositiveFinite(PositionTolerance, nameof(PositionTolerance));
        RequirePositiveFinite(VelocityTolerance, nameof(VelocityTolerance));
        double frequency = 2 * Math.PI / ResponseSeconds;
        if (!double.IsFinite(frequency * frequency)) throw new ArgumentOutOfRangeException(nameof(ResponseSeconds));
        if (!double.IsFinite(2 * frequency * DampingRatio)) throw new ArgumentOutOfRangeException(nameof(DampingRatio));
    }

    private static void RequirePositiveFinite(double value, string name) {
        if (!double.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(name);
    }
}

/// <summary>Shared native motion tokens; all shell surfaces use the same spatial spring.</summary>
public static class MotionTokens {
    public static SpringParameters Spatial { get; } = new(.4, 1);
    public static SpringParameters Reveal { get; } = new(.22, 1);
    public static SpringParameters Toggle => Reveal;
    public static SpringParameters Press { get; } = new(.16, 1);
    public const double PressedScale = .98;
    public const double RevealScale = .97;
    public const double PopoverTravel = 8;
    public const double PageTravel = 8;
    public const double LayerTravel = 8;
    public const double AuxiliaryTravel = 8;
    public const double RailTravel = 8;
    public const double VisibilityThreshold = .001;
}

/// <summary>
/// A scalar, velocity-preserving spring. Values use the caller's units and velocities use units/second.
/// Analytical integration keeps the trajectory independent of the display refresh rate.
/// </summary>
public sealed class SpringValue {
    public SpringValue(double value, SpringParameters? parameters = null, double initialVelocity = 0) {
        RequireFinite(value, nameof(value));
        RequireFinite(initialVelocity, nameof(initialVelocity));
        Parameters = parameters ?? MotionTokens.Spatial;
        Parameters.Validate();
        Value = Target = value;
        Velocity = initialVelocity;
    }

    public SpringParameters Parameters { get; }
    public double Value { get; private set; }
    public double Velocity { get; private set; }
    public double Target { get; private set; }
    public bool IsAtRest => Math.Abs(Value - Target) <= Parameters.PositionTolerance &&
                            Math.Abs(Velocity) <= Parameters.VelocityTolerance;

    /// <summary>Changes only the destination. Neither the presentation value nor its velocity is reset.</summary>
    public void Retarget(double target) {
        RequireFinite(target, nameof(target));
        if (!double.IsFinite(Value - target)) throw new ArgumentOutOfRangeException(nameof(target));
        Target = target;
    }

    public void Snap(double value) {
        RequireFinite(value, nameof(value));
        Value = Target = value;
        Velocity = 0;
    }

    public bool Advance(TimeSpan elapsed) => Advance(elapsed.TotalSeconds);

    /// <summary>Advances by an elapsed interval in seconds; returns true while another frame is needed.</summary>
    public bool Advance(double elapsedSeconds) {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (elapsedSeconds == 0) return !IsAtRest;
        if (IsAtRest) { Snap(Target); return false; }

        double displacement = Value - Target;
        double velocity = Velocity;
        double frequency = 2 * Math.PI / Parameters.ResponseSeconds;
        double damping = Parameters.DampingRatio;
        double nextDisplacement, nextVelocity;

        if (damping == 1) {
            double exponent = frequency * elapsedSeconds;
            double decay = Math.Exp(-exponent);
            if (decay == 0) { Snap(Target); return false; }
            double combined = velocity + frequency * displacement;
            nextDisplacement = decay * (displacement + combined * elapsedSeconds);
            nextVelocity = decay * (velocity - frequency * combined * elapsedSeconds);
        } else if (damping < 1) {
            double attenuation = damping * frequency;
            double dampedFrequency = frequency * Math.Sqrt(1 - damping * damping);
            double decay = Math.Exp(-attenuation * elapsedSeconds);
            if (decay == 0) { Snap(Target); return false; }
            double phase = dampedFrequency * elapsedSeconds;
            double cosine = Math.Cos(phase), sine = Math.Sin(phase);
            nextDisplacement = decay * (displacement * cosine +
                (velocity + attenuation * displacement) / dampedFrequency * sine);
            nextVelocity = decay * (velocity * cosine -
                (attenuation * velocity + frequency * frequency * displacement) / dampedFrequency * sine);
        } else {
            // Negative real roots avoid cosh/sinh overflow after a long suspension.
            double rootRatio = Math.Sqrt(damping - 1) * Math.Sqrt(damping + 1) / damping;
            double slowRoot = -(frequency / damping) / (1 + rootRatio);
            double fastRoot = -frequency * damping * (1 + rootRatio);
            double slowDecay = Math.Exp(slowRoot * elapsedSeconds);
            double fastDecay = Math.Exp(fastRoot * elapsedSeconds);
            if (slowDecay == 0 && fastDecay == 0) { Snap(Target); return false; }
            double cosine = (slowDecay + fastDecay) / 2;
            // expm1 avoids subtracting two almost equal roots/decays near critical damping.
            double sineOverFrequency = slowDecay * -ExpMinusOne((fastRoot - slowRoot) * elapsedSeconds) /
                (slowRoot - fastRoot);
            double attenuation = damping * frequency;
            nextDisplacement = cosine * displacement + sineOverFrequency * (velocity + attenuation * displacement);
            nextVelocity = cosine * velocity - sineOverFrequency * (attenuation * velocity + frequency * frequency * displacement);
        }

        Value = Target + nextDisplacement;
        Velocity = nextVelocity;
        if (IsAtRest) Snap(Target);
        return !IsAtRest;
    }

    private static void RequireFinite(double value, string name) {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(name);
    }

    private static double ExpMinusOne(double value) => Math.Abs(value) < .00001
        ? value * (1 + value * (.5 + value * (1.0 / 6 + value * (1.0 / 24 + value / 120))))
        : Math.Exp(value) - 1;
}
