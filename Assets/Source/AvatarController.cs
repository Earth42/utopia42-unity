using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ReadyPlayerMe;
using Source.Canvas;
using Source.MetaBlocks;
using Source.Model;
using Source.Ui.Profile;
using Source.Utils;
using TMPro;
using UnityEngine;

namespace Source
{
    public class AvatarController : MonoBehaviour
    {
        public const int NoAnimation = 0;

        private const string DefaultAvatarUrl =
            "https://d1a370nemizbjq.cloudfront.net/8b6189f0-c999-4a6a-bffc-7f68d66b39e6.glb"; //FIXME Configuration?
        // "https://d1a370nemizbjq.cloudfront.net/d7a562b0-2378-4284-b641-95e5262e28e5.glb";

        private const string RendererWarningMessage = "Your avatar is too complex. Loading the default...";
        private const string AvatarLoadedMessage = "Avatar loaded";
        private const string AvatarLoadRetryMessage = "Failed to load the avatar. Retrying...";
        private const string AvatarLoadFailedMessage = "Failed to load the avatar. Loading the default...";

        private const int MaxReportDelay = 1; // in seconds 
        private const float AnimationUpdateRate = 0.1f; // in seconds

        private Animator animator;
        private CharacterController controller;
        public GameObject Avatar { private set; get; }
        private string loadingAvatarUrl;
        private int remainingAvatarLoadAttempts;
        public double UpdatedTime;
        private double lastPerformedStateTime;
        private double lastReportedTime;
        private PlayerState lastPerformedState;
        private PlayerState lastReportedState;
        private PlayerState state;
        private Vector3 targetPosition;
        private bool isAnotherPlayer = false;
        public bool Initialized { get; private set; }

        private int animIDSpeed;
        private int animIDGrounded;
        private int animIDJump;
        private int animIDFreeFall;
        private int animIDCustom;
        private int animIDCustomChanged;

        private IEnumerator teleportCoroutine;

        private const int Precision = 5;
        private static readonly float FloatPrecision = Mathf.Pow(10, -Precision);
        private Vector3 movement;
        private Vector3 previousMovement;
        [SerializeField] private TextMeshProUGUI nameLabel;
        [SerializeField] private GameObject namePanel;
        private bool controllerDisabled;
        private int previousCustomAnimation;
        public bool AvatarAllowed { get; private set; }

        private bool ControllerEnabled => controller != null && controller.enabled && !controllerDisabled; // TODO!

        public void Start()
        {
            Initialized = false;
            controller = GetComponent<CharacterController>();
            UpdatedTime = -2 * AnimationUpdateRate;
            animIDSpeed = Animator.StringToHash("Speed");
            animIDGrounded = Animator.StringToHash("Grounded");
            animIDJump = Animator.StringToHash("Jump");
            animIDFreeFall = Animator.StringToHash("FreeFall");
            animIDCustom = Animator.StringToHash("Custom");
            animIDCustomChanged = Animator.StringToHash("CustomChanged");
            StartCoroutine(UpdateAnimationCoroutine());
        }

        private void Update()
        {
            namePanel.transform.rotation = Camera.main.transform.rotation;
        }

        private void LoadDefaultAvatar(bool resetAvatarMsg = true)
        {
            ReloadAvatar(DefaultAvatarUrl, false, resetAvatarMsg);
        }

        private IEnumerator LoadAvatarFromWallet(string walletId)
        {
            yield return null;
            ProfileLoader.INSTANCE.load(walletId, profile =>
            {
                if (profile != null && profile.avatarUrl != null && profile.avatarUrl.Length > 0)
                    ReloadAvatar(profile.avatarUrl);
                // ReloadAvatar("https://d1a370nemizbjq.cloudfront.net/d7a562b0-2378-4284-b641-95e5262e28e5.glb"); // complex default
                // ReloadAvatar("https://d1a370nemizbjq.cloudfront.net/3343c701-0f84-4a57-8c0e-eb25724a2133.glb"); // simple with transparent
                else
                    LoadDefaultAvatar();
            }, () => LoadDefaultAvatar());
        }

        private void FixedUpdate()
        {
            if (!isAnotherPlayer || !ControllerEnabled || state is {teleport: true}) return;

            var currentPosition = CurrentPosition();
            var xzVelocity = state is not {sprinting: true}
                ? Player.INSTANCE.walkSpeed
                : Player.INSTANCE.sprintSpeed;

            var xzStepTarget = Vector3.MoveTowards(currentPosition,
                new Vector3(targetPosition.x, currentPosition.y, targetPosition.z),
                xzVelocity * Time.fixedDeltaTime);

            var yStepTarget = Vector3.MoveTowards(currentPosition,
                new Vector3(currentPosition.x, targetPosition.y, currentPosition.z),
                Player.INSTANCE.sprintSpeed * Time.fixedDeltaTime); // TODO: enhance falling speed

            var m = new Vector3(xzStepTarget.x, yStepTarget.y, xzStepTarget.z) - currentPosition;
            if (!controller.isGrounded && state is {floating: false})
                m += 2 * FloatPrecision * Vector3.down;
            m = Vectors.Truncate(m, Precision);

            var xzMovementMagnitude =
                new Vector3(targetPosition.x - currentPosition.x, 0, targetPosition.z - currentPosition.z)
                    .magnitude;
            controller.Move(m);

            // if (Avatar != null
            //     // && xzMovementMagnitude < FloatPrecision
            //     // && PlayerState.Equals(state, lastPerformedState, true)
            //     && Time.fixedUnscaledTimeAsDouble - lastPerformedStateTime > AnimationUpdateRate
            //    )
            //     SetSpeed(controller.velocity.magnitude);

            if (Avatar != null && controller.isGrounded)
            {
                SetGrounded(true);
                SetFreeFall(false);
                SetJump(false);
            }
        }

        public void SetAnotherPlayer(string walletId, Vector3 position, bool makeVisible)
        {
            Initialized = true;
            isAnotherPlayer = true;
            AvatarAllowed = false;
            ProfileLoader.INSTANCE.load(walletId,
                profile => nameLabel.text = profile?.name ?? MakeWalletShorter(walletId),
                () => nameLabel.text = MakeWalletShorter(walletId));
            if (makeVisible)
                LoadAnotherPlayerAvatar(walletId);
            var target = Vectors.Truncate(position, Precision);
            SetTargetPosition(target);
        }

        public void LoadAnotherPlayerAvatar(string walletId)
        {
            AvatarAllowed = true;
            StartCoroutine(LoadAvatarFromWallet(walletId));
        }

        private IEnumerator SetTransformPosition(Vector3 position)
        {
            controllerDisabled = true;
            yield return null;
            var active = Avatar != null && Avatar.activeSelf;
            SetAvatarBodyActive(false);
            controller.enabled = false;
            yield return null;
            transform.position = position;
            controller.enabled = true;
            if (active) SetAvatarBodyActive(true);
            controllerDisabled = false;
        }

        public void SetMainPlayer(string walletId)
        {
            Initialized = true;
            isAnotherPlayer = false;
            AvatarAllowed = true;
            namePanel.gameObject.SetActive(false);
            StartCoroutine(LoadAvatarFromWallet(walletId));
            SetTargetPosition(transform.position);
        }

        private string MakeWalletShorter(string walletId)
        {
            return walletId[..6] + "..." + walletId[^5..];
        }

        private void UpdateLookDirection(Vector3 movement)
        {
            movement.y = 0;
            if (movement.magnitude > FloatPrecision)
                LookAt(movement.normalized);
        }

        private void LookAt(Vector3 forward)
        {
            forward.y = 0;
            Avatar.transform.rotation = Quaternion.LookRotation(forward);
        }

        private void SetTargetPosition(Vector3 target)
        {
            targetPosition = Vectors.Truncate(target, Precision);
        }

        public Vector3 CurrentPosition()
        {
            return Vectors.Truncate(transform.position, Precision);
        }

        public void SetAvatarBodyActive(bool active)
        {
            if (Avatar != null)
                Avatar.SetActive(active);
        }

        public void UpdatePlayerState(PlayerState playerState)
        {
            if (playerState == null || isAnotherPlayer && !ControllerEnabled) return;
            SetTargetPosition(playerState.position.ToVector3());
            SetPlayerState(playerState);
            if (state.teleport)
            {
                if (!PlayerState.Equals(state, lastPerformedState))
                {
                    StartCoroutine(SetTransformPosition(targetPosition));
                    if (!isAnotherPlayer)
                        ReportToServer();
                }

                SetLastPerformedState(state);
                return;
            }

            var pos = state.GetPosition();
            previousMovement = movement;
            movement = pos - (lastPerformedState?.GetPosition() ?? pos);
            if (Avatar != null)
                UpdateLookDirection(movement);

            var forceAnimateAndReport =
                playerState.customAnimationNumber != NoAnimation ||
                playerState.teleport != lastPerformedState?.teleport ||
                playerState.floating != lastPerformedState?.floating ||
                playerState.jump != lastPerformedState?.jump;

            if ((isAnotherPlayer || forceAnimateAndReport) && Avatar != null && ControllerEnabled)
            {
                UpdateAnimation();
            }

            if (!isAnotherPlayer && (forceAnimateAndReport ||
                                     movement.magnitude > FloatPrecision &&
                                     previousMovement.magnitude < FloatPrecision))
                ReportToServer();
        }

        private void SetLastPerformedState(PlayerState playerState)
        {
            lastPerformedState = playerState;
            lastPerformedStateTime = Time.unscaledTimeAsDouble;
        }

        private IEnumerator UpdateAnimationCoroutine()
        {
            var wasMoving = false;
            while (true)
            {
                yield return null;
                if (isAnotherPlayer)
                    yield break;

                if (Time.unscaledTimeAsDouble - lastPerformedStateTime > AnimationUpdateRate
                    && !PlayerState.Equals(state, lastPerformedState) && ControllerEnabled && state != null)
                {
                    var moving = movement.magnitude > FloatPrecision;
                    if (wasMoving && !moving)
                        ReportToServer();

                    wasMoving = moving;
                    if (Avatar != null)
                        UpdateAnimation();
                }

                if (state != null && Time.unscaledTimeAsDouble - lastReportedTime >
                    (PlayerState.Equals(lastReportedState, state, true)
                        ? MaxReportDelay
                        : AnimationUpdateRate))
                    ReportToServer();
            }
        }

        private void SetPlayerState(PlayerState playerState)
        {
            state = playerState;
        }

        private void UpdateAnimation()
        {
            StartCoroutine(SetCustomAnimation(state?.customAnimationNumber ?? NoAnimation));

            var grounded = controller.isGrounded;
            if (grounded)
            {
                SetJump(false);
                SetFreeFall(false);
            }

            if (state is {jump: true} && !PlayerState.Equals(state, lastPerformedState))
                SetJump(true);
            SetFreeFall(state is {floating: true} ||
                        Mathf.Abs(state.velocityY) > Player.INSTANCE.MinFreeFallSpeed && !grounded);

            SetGrounded(state is not {floating: true} && grounded);

            SetSpeed(movement.magnitude < FloatPrecision ? 0 :
                state.sprinting ? Player.INSTANCE.sprintSpeed : Player.INSTANCE.walkSpeed);

            SetLastPerformedState(state);
        }

        private void ReportToServer()
        {
            BrowserConnector.INSTANCE.ReportPlayerState(state);
            lastReportedState = state;
            lastReportedTime = Time.unscaledTimeAsDouble;
            Player.INSTANCE.mainPlayerStateReport.Invoke(state);
        }

        public void ReloadAvatar(string url, bool ignorePreviousUrl = false,
            bool resetAvatarMsg = true)
        {
            if (url == null || !ignorePreviousUrl && url.Equals(loadingAvatarUrl) ||
                remainingAvatarLoadAttempts != 0) return;
            if (resetAvatarMsg)
                GameManager.INSTANCE.ResetAvatarMsg();
            remainingAvatarLoadAttempts = 3;
            loadingAvatarUrl = url;

            if (this != null)
                AvatarLoader.INSTANCE.AddJob(gameObject, url, OnAvatarLoad, OnAvatarLoadFailure);
        }

        private void OnAvatarLoadFailure(FailureType failureType)
        {
            remainingAvatarLoadAttempts -= 1;
            if (remainingAvatarLoadAttempts > 0 && failureType != FailureType.UrlProcessError)
            {
                Debug.LogWarning(
                    $"{state?.walletId} | {failureType} : {AvatarLoadRetryMessage} | Remaining attempts: " +
                    remainingAvatarLoadAttempts);
                if (!isAnotherPlayer)
                    GameManager.INSTANCE.ShowAvatarStateMessage(AvatarLoadRetryMessage, false);
                AvatarLoader.INSTANCE.AddJob(gameObject, loadingAvatarUrl, OnAvatarLoad, OnAvatarLoadFailure);
            }
            else
            {
                Debug.LogWarning(
                    $"{state?.walletId} | {failureType} : {AvatarLoadFailedMessage}");
                if (!isAnotherPlayer)
                    GameManager.INSTANCE.ShowAvatarStateMessage(AvatarLoadFailedMessage, true);
                remainingAvatarLoadAttempts = 0;
                LoadDefaultAvatar(false);
            }
        }

        private void OnAvatarLoad(GameObject avatar)
        {
            if (avatar.GetComponentsInChildren<Renderer>().Length > 2)
            {
                Debug.LogWarning($"{state?.walletId} | {RendererWarningMessage}");
                if (!isAnotherPlayer)
                    GameManager.INSTANCE.ShowAvatarStateMessage(RendererWarningMessage, true);
                MetaBlockObject.DeepDestroy3DObject(avatar);
                remainingAvatarLoadAttempts = 0;
                LoadDefaultAvatar(false);
                return;
            }

            if (isAnotherPlayer)
                Debug.Log($"{state?.walletId} | {AvatarLoadedMessage}");
            PrepareAvatar(avatar);
            animator = Avatar.GetComponentInChildren<Animator>();
            remainingAvatarLoadAttempts = 0;
        }

        private void PrepareAvatar(GameObject go)
        {
            if (Avatar != null)
                MetaBlockObject.DeepDestroy3DObject(Avatar);

            if (isAnotherPlayer)
            {
                go.transform.SetParent(transform);
                go.transform.localPosition = Vector3.zero;
                Avatar = go;
                return;
            }

            var container = new GameObject {name = "AvatarContainer"};
            container.transform.SetParent(transform);
            container.transform.localPosition = Vector3.zero;
            go.transform.SetParent(container.transform);
            go.transform.localPosition = new Vector3(.06f, 0, -.02f);
            Avatar = container;
        }

        private IEnumerator SetCustomAnimation(int animationNumber)
        {
            if (animator.GetCurrentAnimatorStateInfo(0).IsName("Custom"))
            {
                if (previousCustomAnimation != animationNumber)
                {
                    animator.SetBool(animIDCustomChanged, true);
                    yield return null;
                    yield return SetCustomAnimation(animationNumber);
                }

                yield break;
            }

            animator.SetBool(animIDCustomChanged, false);

            if (animationNumber == NoAnimation)
            {
                animator.SetBool(animIDCustom, false);
                previousCustomAnimation = animationNumber;
                yield break;
            }


            var aoc = new AnimatorOverrideController(animator.runtimeAnimatorController);
            var anims = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            foreach (var animationClip in Player.INSTANCE.customAvatarAnimations)
                anims.Add(new KeyValuePair<AnimationClip, AnimationClip>(animationClip,
                    Player.INSTANCE.customAvatarAnimations[animationNumber - 1]));
            aoc.ApplyOverrides(anims);
            animator.runtimeAnimatorController = aoc;
            animator.SetBool(animIDCustom, true);
            previousCustomAnimation = animationNumber;
        }

        private void SetJump(bool jump)
        {
            animator.SetBool(animIDJump, jump);
        }

        private void SetFreeFall(bool freeFall)
        {
            animator.SetBool(animIDFreeFall, freeFall);
        }

        private void SetGrounded(bool grounded)
        {
            animator.SetBool(animIDGrounded, grounded);
        }

        private void SetSpeed(float speed)
        {
            animator.SetFloat(animIDSpeed, speed);
        }

        private float? GetSpeed()
        {
            return Avatar == null ? null : animator.GetFloat(animIDSpeed);
        }

        private void OnDestroy()
        {
            if (Avatar != null)
                MetaBlockObject.DeepDestroy3DObject(Avatar);
            if (nameLabel != null)
                DestroyImmediate(nameLabel);
            if (namePanel != null)
                DestroyImmediate(namePanel);
        }

        public class PlayerState
        {
            public string rid;
            public string walletId;
            public SerializableVector3 position;
            public bool floating;
            public bool jump;
            public bool sprinting;
            public float velocityY;
            public bool teleport;
            public int customAnimationNumber;

            public PlayerState(string walletId, SerializableVector3 position,
                bool floating, bool jump,
                bool sprinting, float velocityY, bool teleport, int customAnimationNumber = 0)
            {
                // rid = random.Next(0, int.MaxValue);
                rid = Guid.NewGuid().ToString();
                this.walletId = walletId;
                this.position = position;
                this.floating = floating;
                this.jump = jump;
                this.sprinting = sprinting;
                this.velocityY = velocityY;
                this.teleport = teleport;
                this.customAnimationNumber = customAnimationNumber;
            }

            private PlayerState(string walletId, SerializableVector3 position)
            {
                this.walletId = walletId;
                this.position = position;
                teleport = true;
            }

            public static PlayerState CreateTeleportState(int network, string contract, string walletId,
                Vector3 position)
            {
                return new PlayerState(walletId, new SerializableVector3(position));
            }

            public Vector3 GetPosition()
            {
                return position.ToVector3();
            }

            public static bool Equals(PlayerState s1, PlayerState s2, bool ignoreId = false)
            {
                return s1 != null
                       && s2 != null
                       && s1.walletId == s2.walletId
                       && (ignoreId || s1.rid == s2.rid)
                       && Equals(s1.position, s2.position)
                       && s1.floating == s2.floating
                       && s1.jump == s2.jump
                       && s1.sprinting == s2.sprinting
                       && s1.teleport == s2.teleport
                       && s1.customAnimationNumber == s2.customAnimationNumber
                       && Math.Abs(s1.velocityY - s2.velocityY) < FloatPrecision;
            }
        }
    }
}