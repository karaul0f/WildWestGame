using System;
using Unigine;

public partial class CameraRig : Component
{
	#region Editor parameters

	[ShowInEditor]
	[Parameter(Group = "Bob", Tooltip = "Enable head bob while moving on the ground")]
	private bool enableBob = true;

	[ShowInEditor]
	[Parameter(Group = "Bob", Tooltip = "Distance covered by one full bob cycle (two steps), meters.\nPhase is driven by distance, not time, so the bob stays in sync with the actual speed.")]
	private float strideLength = 1.9f;

	[ShowInEditor]
	[Parameter(Group = "Bob", Tooltip = "Speed at which the bob reaches full amplitude, m/s.\nMatch it to the FPC running speed (6 by default).")]
	private float referenceSpeed = 6.0f;

	[ShowInEditor]
	[ParameterSlider(Min = 0.0f, Max = 0.2f, Group = "Bob", Tooltip = "Vertical amplitude at full speed, meters")]
	private float verticalAmplitude = 0.055f;

	[ShowInEditor]
	[ParameterSlider(Min = 0.0f, Max = 0.2f, Group = "Bob", Tooltip = "Sideways amplitude at full speed, meters")]
	private float horizontalAmplitude = 0.035f;

	[ShowInEditor]
	[Parameter(Group = "Bob", Tooltip = "Speed below which the bob is not applied at all, m/s")]
	private float minSpeed = 0.4f;

	[ShowInEditor]
	[ParameterSlider(Min = 0.0f, Max = 2.0f, Group = "Bob", Tooltip = "Amplitude multiplier while crouching")]
	private float crouchScale = 0.45f;

	[ShowInEditor]
	[Parameter(Group = "Bob", Tooltip = "How fast the bob fades in and out when starting and stopping.\nHigher is snappier.")]
	private float blendSpeed = 12.0f;

	[ShowInEditor]
	[Parameter(Group = "Landing", Tooltip = "Dip the camera when hitting the ground after a fall")]
	private bool enableLanding = true;

	[ShowInEditor]
	[Parameter(Group = "Landing", Tooltip = "Dip depth per m/s of impact speed, meters")]
	private float landingDipPerSpeed = 0.012f;

	[ShowInEditor]
	[Parameter(Group = "Landing", Tooltip = "Maximum dip depth, meters")]
	private float maxLandingDip = 0.16f;

	[ShowInEditor]
	[Parameter(Group = "Landing", Tooltip = "Spring stiffness returning the camera after a landing")]
	private float landingStiffness = 140.0f;

	[ShowInEditor]
	[Parameter(Group = "Landing", Tooltip = "Spring damping. Higher settles faster with less bounce.")]
	private float landingDamping = 16.0f;

	#endregion

	#region Public state

	public vec3 Offset { get; private set; } = vec3.ZERO;

	public float StridePhase { get; private set; }

	public event Action<int> Footstep;

	public bool IsMoving { get; private set; }

	#endregion

	private FirstPersonController controller;

	private float bobWeight;
	private float landingOffset;
	private float landingVelocity;
	private bool wasGrounded = true;
	private float previousVerticalVelocity;

	private void Init()
	{
		controller = ComponentSystem.GetComponent<FirstPersonController>(node, true);
		if (controller == null)
			controller = ComponentSystem.FindComponentInWorld<FirstPersonController>(true);

		if (controller == null)
		{
			Log.Error("CameraRig: no FirstPersonController found, the rig has nothing to drive\n");
			Enabled = false;
			return;
		}

		wasGrounded = controller.IsGround;
	}

	private void Update()
	{
		if (controller == null || !controller.IsInitialized)
			return;

		float dt = Game.IFps;

		vec3 bob = UpdateBob(dt);
		float landing = UpdateLanding(dt);

		Offset = bob + new vec3(0.0f, 0.0f, landing);
		controller.AdditionalCameraOffset = Offset;
	}

	private void Shutdown()
	{
		if (controller != null)
			controller.AdditionalCameraOffset = vec3.ZERO;
	}

	#region Bob

	private vec3 UpdateBob(float dt)
	{
		float speed = (float)controller.HorizontalVelocity.Length;
		bool grounded = controller.IsGround;

		IsMoving = grounded && speed > minSpeed;

		float target = enableBob && IsMoving ? 1.0f : 0.0f;
		bobWeight = Damp(bobWeight, target, blendSpeed, dt);

		if (IsMoving)
		{
			float previousPhase = StridePhase;
			StridePhase += speed * dt / MathLib.Max(0.1f, strideLength) * MathLib.PI2;

			EmitFootsteps(previousPhase, StridePhase);

			if (StridePhase > MathLib.PI2)
				StridePhase -= MathLib.PI2;
		}

		float speedFactor = MathLib.Saturate(speed / MathLib.Max(0.1f, referenceSpeed));
		float scale = bobWeight * speedFactor * (controller.IsCrouch ? crouchScale : 1.0f);

		float vertical = MathLib.Sin(StridePhase * 2.0f) * verticalAmplitude * scale;
		float horizontal = MathLib.Sin(StridePhase) * horizontalAmplitude * scale;

		return new vec3(horizontal, 0.0f, vertical);
	}

	private void EmitFootsteps(float from, float to)
	{
		if (Footstep == null)
			return;

		CheckCrossing(from, to, MathLib.PI * 0.5f, 0);
		CheckCrossing(from, to, MathLib.PI * 1.5f, 1);
	}

	private void CheckCrossing(float from, float to, float threshold, int foot)
	{
		if (from < threshold && to >= threshold)
			Footstep?.Invoke(foot);
	}

	#endregion

	#region Landing

	private float UpdateLanding(float dt)
	{
		bool grounded = controller.IsGround;

		if (enableLanding && grounded && !wasGrounded)
		{
			float impact = MathLib.Abs(previousVerticalVelocity);
			float dip = MathLib.Min(impact * landingDipPerSpeed, maxLandingDip);
			landingVelocity -= dip * landingStiffness * 0.06f;
		}

		wasGrounded = grounded;
		previousVerticalVelocity = controller.VerticalVelocity;

		if (landingOffset == 0.0f && landingVelocity == 0.0f)
			return 0.0f;

		int steps = MathLib.Clamp((int)(dt / 0.008f) + 1, 1, 8);
		float h = dt / steps;
		for (int i = 0; i < steps; i++)
		{
			landingVelocity += (-landingStiffness * landingOffset - landingDamping * landingVelocity) * h;
			landingOffset += landingVelocity * h;
		}

		if (MathLib.Abs(landingOffset) < 0.0005f && MathLib.Abs(landingVelocity) < 0.005f)
		{
			landingOffset = 0.0f;
			landingVelocity = 0.0f;
		}

		return landingOffset;
	}

	#endregion

	private static float Damp(float current, float target, float speed, float dt)
	{
		return MathLib.Lerp(current, target, MathLib.Saturate(1.0f - MathLib.Exp(-speed * dt)));
	}
}
