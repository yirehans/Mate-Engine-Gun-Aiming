using System;
using System.Collections.Generic;
using UnityEngine;
using UniVRM10;

[Serializable]
public class TrackingPermission
{
    public string stateOrParameterName;
    public bool isParameter;
    public bool allowHead = true, allowSpine = true, allowEye = true;
}

[RequireComponent(typeof(Animator))]
public class AvatarMouseTracking : MonoBehaviour
{
    [Header("Mouse Tracking Settings")]
    public bool enableMouseTracking = true;
    public List<TrackingPermission> trackingPermissions = new();

    [Range(0f, 90f)] public float headYawLimit = 45f, headPitchLimit = 30f;
    [Range(1f, 20f)] public float headSmoothness = 10f;
    [Range(-90f, 90f)] public float spineMinRotation = -15f, spineMaxRotation = 15f;
    [Range(1f, 50f)] public float spineSmoothness = 25f;
    [Range(1f, 10f)] public float spineFadeSpeed = 5f;
    [Range(0f, 90f)] public float eyeYawLimit = 12f, eyePitchLimit = 12f;
    [Range(1f, 20f)] public float eyeSmoothness = 10f;
    [Range(0f, 1f)] public float headBlend = 1f, spineBlend = 1f, eyeBlend = 1f;

    Animator animator;
    Camera mainCam;

    Transform headBone, spineBone, chestBone, upperChestBone;
    Transform leftEyeBone, rightEyeBone, headDriver, spineDriver;
    Transform leftEyeDriver, rightEyeDriver, eyeCenter, vrmLookAtTarget;

    Quaternion headInitRot, spineInitRot;
    float spineTrackingWeight;

    Vrm10Instance vrm10;
    int currStateHash, nextStateHash;
    //
    [Header("Arms Tracking")]
    public float armBlend = 1f;
    public float armFadeSpeed = 5f;
    public float armSmoothness = 5f;
    //public float armMinPitch = -20f;
    public float armMinPitch = 0f;
    public float armMaxPitch = 40f;

    private float armTrackingWeight;
    private Quaternion upperArmRInitRot;
    private Quaternion upperArmLInitRot;
    private Transform upperArmR, upperArmL;
    private Transform rightHand;
    private Transform upperArmRDriver, upperArmLDriver;
    private Transform rightUpperArmBone, leftUpperArmBone;
    public float ikPositionWeight = 1f;     // how strongly IK moves the hand
    public float ikRotationWeight = 1f;     // how strongly IK rotates the hand
    public float ikSmoothing = 10f;         // smoothing for target moves
    public float maxAimDistance = 30f;
    public Transform muzzle;                // optional: if your gun prefab has a muzzle transform
    public bool debugRays = true;

    private Vector3 smoothedTarget;
    private Vector3 armRestForward;

    //
    //[SerializeField] GameObject gunPrefab;
    public GameObject gunPrefab;
    bool wasArmed = false;
    GameObject gun;

    void Start()
    {
        animator = GetComponent<Animator>();
        mainCam = Camera.main;
        if (!animator || !animator.isHuman) { enableMouseTracking = false; Debug.LogError("Animator not found or not humanoid!"); return; }
        vrm10 = GetComponentInChildren<Vrm10Instance>();
        InitHead(); InitSpine(); InitEye(); InitArms();
        Debug.Log("guncheck");
        //foreach (Component c in vrm10.GetComponentIndex(1)) {

        Debug.Log($"Gun prefab is {(gunPrefab ? "SET" : "NULL")}");
        //}
        //var avatar = Instantiate(avatarPrefab);
        var tracking = GetComponent<AvatarMouseTracking>();
        tracking.gunPrefab = gunPrefab;
        if (tracking != null)
        {
            Debug.Log("tracking");
            if (tracking.gunPrefab != null)
            {
                Debug.Log("trackinggun");
            }
        }
        //Component tracking = GetComponent<AvatarMouseTracking>();
        //tracking.gunPrefab = gunPrefab;

        if (gunPrefab)
        {
            Debug.Log("gun");
            gun = Instantiate(gunPrefab, rightHand);
            gun.transform.localPosition = Vector3.zero;
            gun.transform.localRotation = Quaternion.identity;
            //gun.SetActive(false); // invisible until armed
            gun.SetActive(true); // invisible until armed
        }
        else
        {
            Debug.Log("no gun");
        }
    }

    void InitHead()
    {
        headBone = animator.GetBoneTransform(HumanBodyBones.Head);
        if (!headBone) return;
        headDriver = new GameObject("HeadDriver").transform;
        headDriver.SetParent(headBone.parent, false);
        headDriver.localPosition = headBone.localPosition;
        headDriver.localRotation = headBone.localRotation;
        headInitRot = headBone.localRotation;
    }

    void InitSpine()
    {
        spineBone = animator.GetBoneTransform(HumanBodyBones.Spine);
        chestBone = animator.GetBoneTransform(HumanBodyBones.Chest);
        upperChestBone = animator.GetBoneTransform(HumanBodyBones.UpperChest);
        if (!spineBone) return;
        spineDriver = new GameObject("SpineDriver").transform;
        spineDriver.SetParent(spineBone.parent, false);
        spineDriver.localPosition = spineBone.localPosition;
        spineDriver.localRotation = spineBone.localRotation;
        spineInitRot = spineBone.localRotation;
    }
    void InitArms()
    {
        // Right arm
        upperArmR = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Transform lowerArmR = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        Transform handR = animator.GetBoneTransform(HumanBodyBones.RightHand);
        //rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);

        if (upperArmR)
        {
            Transform rightArmDriver = new GameObject("RightArmDriver").transform;
            rightArmDriver.SetParent(upperArmR.parent, false);
            rightArmDriver.localPosition = upperArmR.localPosition;
            rightArmDriver.localRotation = upperArmR.localRotation;
            upperArmRDriver = rightArmDriver; // store if you want to drive it later
            upperArmRInitRot = upperArmR.localRotation;
        }

        // Left arm
        upperArmL = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform lowerArmL = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        Transform handL = animator.GetBoneTransform(HumanBodyBones.LeftHand);

        if (upperArmL)
        {
            Transform leftArmDriver = new GameObject("LeftArmDriver").transform;
            leftArmDriver.SetParent(upperArmL.parent, false);
            leftArmDriver.localPosition = upperArmL.localPosition;
            leftArmDriver.localRotation = upperArmL.localRotation;
            upperArmLDriver = leftArmDriver;
            upperArmLInitRot = upperArmL.localRotation;
        }
        //upperArmR = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        //upperArmRDriver = new GameObject("UpperArmRDriver").transform;
        //upperArmRDriver.SetParent(upperArmR.parent, false);
        //upperArmRDriver.localPosition = upperArmR.localPosition;
        //upperArmRDriver.localRotation = upperArmR.localRotation;
        //upperArmRInitRot = upperArmR.localRotation;

        //// Cache the arm's rest forward direction (in world space)
        //armRestForward = upperArmR.TransformDirection(Vector3.back);
    }

    void InitEye()
    {
        leftEyeBone = animator.GetBoneTransform(HumanBodyBones.LeftEye);
        rightEyeBone = animator.GetBoneTransform(HumanBodyBones.RightEye);
        if (vrm10)
        {
            vrmLookAtTarget = new GameObject("VRMLookAtTarget").transform;
            vrmLookAtTarget.SetParent(transform, false);
            vrm10.LookAtTarget = vrmLookAtTarget;
            vrm10.LookAtTargetType = VRM10ObjectLookAt.LookAtTargetTypes.YawPitchValue;
        }
        if (!leftEyeBone || !rightEyeBone)
        {
            foreach (var t in animator.GetComponentsInChildren<Transform>())
            {
                var n = t.name.ToLower();
                if (!leftEyeBone && (n.Contains("lefteye") || n.Contains("eye.l"))) leftEyeBone = t;
                else if (!rightEyeBone && (n.Contains("righteye") || n.Contains("eye.r"))) rightEyeBone = t;
            }
        }
        if (leftEyeBone && rightEyeBone)
        {
            eyeCenter = new GameObject("EyeCenter").transform;
            eyeCenter.SetParent(leftEyeBone.parent, false);
            eyeCenter.position = (leftEyeBone.position + rightEyeBone.position) * 0.5f;
            leftEyeDriver = new GameObject("LeftEyeDriver").transform;
            leftEyeDriver.SetParent(leftEyeBone.parent, false);
            leftEyeDriver.localPosition = leftEyeBone.localPosition;
            leftEyeDriver.localRotation = leftEyeBone.localRotation;
            rightEyeDriver = new GameObject("RightEyeDriver").transform;
            rightEyeDriver.SetParent(rightEyeBone.parent, false);
            rightEyeDriver.localPosition = rightEyeBone.localPosition;
            rightEyeDriver.localRotation = rightEyeBone.localRotation;
        }
    }

    void LateUpdate()
    {
        if (!enableMouseTracking || !mainCam || !animator) return;
        //
        bool mouseUpper = Input.mousePosition.y > Screen.height / 3;
        bool isArmed = animator.GetBool("isArmed");

        if (mouseUpper && !isArmed)
        {
            animator.SetBool("isArmed", true);
            //SetGunVisible(true);
        }
        else if (!mouseUpper && isArmed)
        {
            animator.SetBool("isArmed", false);
            //SetGunVisible(false);
        }
        //
        var info = animator.GetCurrentAnimatorStateInfo(0);
        var next = animator.GetNextAnimatorStateInfo(0);
        bool trans = animator.IsInTransition(0);
        if (trans) nextStateHash = next.shortNameHash;
        else { currStateHash = info.shortNameHash; nextStateHash = 0; }

        if (IsAllowed("Head")) DoHead();
        DoSpine();
        if (IsAllowed("Eye")) DoEye();
        if (isArmed)
        {
            DoArms();
        }
    }

    bool IsAllowed(string f)
    {
        bool? a = null, b = null;
        foreach (var t in trackingPermissions)
        {
            if (t.isParameter && animator.GetBool(t.stateOrParameterName)) return Get(t, f);
            int hash = Animator.StringToHash(t.stateOrParameterName);
            if (currStateHash == hash) a = Get(t, f);
            if (animator.IsInTransition(0) && nextStateHash == hash) b = Get(t, f);
        }
        if (animator.IsInTransition(0) && b.HasValue) return b.Value;
        return a ?? false;
    }
    bool Get(TrackingPermission e, string f) => f == "Head" ? e.allowHead : f == "Spine" ? e.allowSpine : e.allowEye;

    void DoHead()
    {
        if (!headBone || !headDriver) return;
        var mouse = Input.mousePosition;
        var world = mainCam.ScreenToWorldPoint(new Vector3(mouse.x, mouse.y, mainCam.nearClipPlane));
        var dir = (world - headDriver.position).normalized;
        var localDir = headDriver.parent.InverseTransformDirection(dir);
        float yaw = Mathf.Clamp(Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg, -headYawLimit, headYawLimit);
        float pitch = Mathf.Clamp(Mathf.Asin(localDir.y) * Mathf.Rad2Deg, -headPitchLimit, headPitchLimit);
        headDriver.localRotation = Quaternion.Slerp(headDriver.localRotation, Quaternion.Euler(-pitch, yaw, 0), Time.deltaTime * headSmoothness);
        var baseRot = headBone.localRotation;
        var delta = headDriver.localRotation * Quaternion.Inverse(headInitRot);
        headBone.localRotation = Quaternion.Slerp(baseRot, delta * baseRot, headBlend);
    }

    void DoSpine()
    {
        if (!spineBone || !spineDriver) return;
        float targetW = IsAllowed("Spine") ? 1f : 0f;
        spineTrackingWeight = Mathf.MoveTowards(spineTrackingWeight, targetW, Time.deltaTime * spineFadeSpeed);
        float normY = Mathf.Clamp01(Input.mousePosition.x / Screen.width);
        float targetY = Mathf.Lerp(spineMinRotation, spineMaxRotation, normY);
        spineDriver.localRotation = Quaternion.Slerp(spineDriver.localRotation, Quaternion.Euler(0f, -targetY, 0f), Time.deltaTime * spineSmoothness);
        var baseRot = spineBone.localRotation;
        var delta = spineDriver.localRotation * Quaternion.Inverse(spineInitRot);
        float applied = spineTrackingWeight * spineBlend;
        var offset = Quaternion.Slerp(Quaternion.identity, delta, applied);
        spineBone.localRotation = offset * baseRot;
        if (chestBone)
            chestBone.localRotation = Quaternion.Slerp(Quaternion.identity, delta, 0.8f * applied) * chestBone.localRotation;
        if (upperChestBone)
            upperChestBone.localRotation = Quaternion.Slerp(Quaternion.identity, delta, 0.6f * applied) * upperChestBone.localRotation;
    }
    void DoArms()
    {
        float aimDistance = 10f;
        float armYawLimit = 1f;
        float armPitchLimit = 10f;
        if (!upperArmR || !upperArmRDriver) return;
        var mouse = Input.mousePosition;
        Vector3 dirR = (mainCam.ScreenToWorldPoint(new Vector3(mouse.x, mouse.y, aimDistance)) - upperArmR.position).normalized;
        Vector3 dirL = (mainCam.ScreenToWorldPoint(new Vector3(mouse.x, mouse.y, aimDistance)) - upperArmL.position).normalized;
        Vector3 localDirR = upperArmR.parent.InverseTransformDirection(dirR);
        Vector3 localDirL = upperArmL.parent.InverseTransformDirection(dirL);
        float yawR = Mathf.Clamp(Mathf.Atan2(localDirR.x, localDirR.z) * Mathf.Rad2Deg, -armYawLimit, armPitchLimit);
        float pitchR = Mathf.Clamp(-Mathf.Asin(localDirR.y) * Mathf.Rad2Deg, -armPitchLimit, armPitchLimit);
        float yawL = Mathf.Clamp(Mathf.Atan2(localDirL.x, localDirL.z) * Mathf.Rad2Deg, -armYawLimit, armPitchLimit);
        float pitchL = Mathf.Clamp(-Mathf.Asin(localDirL.y) * Mathf.Rad2Deg, -armPitchLimit, armPitchLimit);
        Quaternion targetRotR = Quaternion.Euler(pitchR, yawR, 0);
        Quaternion targetRotL = Quaternion.Euler(pitchR, yawR, 0);
        upperArmRDriver.localRotation = Quaternion.Slerp(upperArmRDriver.localRotation, targetRotR, Time.deltaTime * armSmoothness);
        upperArmLDriver.localRotation = Quaternion.Slerp(upperArmLDriver.localRotation, targetRotL, Time.deltaTime * armSmoothness);
        Quaternion deltaR = upperArmRDriver.localRotation * Quaternion.Inverse(upperArmRInitRot);
        upperArmR.localRotation = Quaternion.Slerp(upperArmR.localRotation, deltaR * upperArmR.localRotation, armBlend);
        Quaternion deltaL = upperArmLDriver.localRotation * Quaternion.Inverse(upperArmLInitRot);
        upperArmL.localRotation = Quaternion.Slerp(upperArmL.localRotation, deltaL * upperArmL.localRotation, armBlend);
    }
    void DoArms1()
    {
        float aimDistance = 10f;
        float armYawLimit = 40f;
        float armPitchLimit = 10f;
        if (!upperArmR || !upperArmRDriver) return;
        var mouse = Input.mousePosition;
        Vector3 dirR = (mainCam.ScreenToWorldPoint(new Vector3(mouse.x, mouse.y, aimDistance)) - upperArmR.position).normalized;
        Vector3 dirL = (mainCam.ScreenToWorldPoint(new Vector3(mouse.x, mouse.y, aimDistance)) - upperArmL.position).normalized;
        Vector3 localDirR = upperArmR.parent.InverseTransformDirection(dirR);
        Vector3 localDirL = upperArmL.parent.InverseTransformDirection(dirL);
        float yawR = Mathf.Clamp(Mathf.Atan2(localDirR.x, localDirR.z) * Mathf.Rad2Deg, -armYawLimit, armYawLimit);
        float yawL = Mathf.Clamp(Mathf.Atan2(-localDirL.x, localDirL.z) * Mathf.Rad2Deg, -armYawLimit, armYawLimit);

        float pitchR = Mathf.Clamp(-Mathf.Asin(localDirR.y) * Mathf.Rad2Deg, -armPitchLimit, armPitchLimit);
        float pitchL = Mathf.Clamp(-Mathf.Asin(localDirL.y) * Mathf.Rad2Deg, -armPitchLimit, armPitchLimit);
        Quaternion targetRotR = Quaternion.Euler(pitchR, yawR, 0);
        Quaternion targetRotL = Quaternion.Euler(pitchL, yawL, 0);
        upperArmRDriver.localRotation = Quaternion.Slerp(upperArmRDriver.localRotation, targetRotR, Time.deltaTime * armSmoothness);
        upperArmLDriver.localRotation = Quaternion.Slerp(upperArmLDriver.localRotation, targetRotL, Time.deltaTime * armSmoothness);
        Quaternion deltaR = upperArmRDriver.localRotation * Quaternion.Inverse(upperArmRInitRot);
        upperArmR.localRotation = Quaternion.Slerp(upperArmR.localRotation, deltaR * upperArmR.localRotation, armBlend);
        Quaternion deltaL = upperArmLDriver.localRotation * Quaternion.Inverse(upperArmLInitRot);
        upperArmL.localRotation = Quaternion.Slerp(upperArmL.localRotation, deltaL * upperArmL.localRotation, armBlend);
    }
    void DoEye()
    {
        var mouse = Input.mousePosition;
        var world = mainCam.ScreenToWorldPoint(new Vector3(mouse.x, mouse.y, mainCam.nearClipPlane));
        if (vrm10 && vrmLookAtTarget)
        {
            vrmLookAtTarget.position = world;
            var par = vrmLookAtTarget.parent ?? transform;
            Matrix4x4 mtx = Matrix4x4.TRS(par.position, par.rotation, Vector3.one);
            var (rawYaw, rawPitch) = mtx.CalcYawPitch(world);
            float yaw = Mathf.Clamp(-rawYaw, -eyeYawLimit, eyeYawLimit);
            float pitch = Mathf.Clamp(rawPitch, -eyePitchLimit, eyePitchLimit);
            var currFwd = vrmLookAtTarget.forward;
            var tgtFwd = Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
            var smooth = Vector3.Slerp(currFwd, tgtFwd, Time.deltaTime * eyeSmoothness);
            vrmLookAtTarget.rotation = Quaternion.LookRotation(smooth);
            return;
        }
        if (!leftEyeBone || !rightEyeBone || !eyeCenter) return;
        eyeCenter.position = (leftEyeBone.position + rightEyeBone.position) * 0.5f;
        var dir = (world - eyeCenter.position).normalized;
        var localDir = eyeCenter.parent.InverseTransformDirection(dir);
        float eyeYaw = Mathf.Clamp(Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg, -eyeYawLimit, eyeYawLimit);
        float eyePitch = Mathf.Clamp(Mathf.Asin(localDir.y) * Mathf.Rad2Deg, -eyePitchLimit, eyePitchLimit);
        var eyeRot = Quaternion.Euler(-eyePitch, eyeYaw, 0f);
        leftEyeDriver.localRotation = Quaternion.Slerp(leftEyeDriver.localRotation, eyeRot, Time.deltaTime * eyeSmoothness);
        rightEyeDriver.localRotation = Quaternion.Slerp(rightEyeDriver.localRotation, eyeRot, Time.deltaTime * eyeSmoothness);
        leftEyeBone.localRotation = Quaternion.Slerp(leftEyeBone.localRotation, leftEyeDriver.localRotation, eyeBlend);
        rightEyeBone.localRotation = Quaternion.Slerp(rightEyeBone.localRotation, rightEyeDriver.localRotation, eyeBlend);
    }

    void OnDestroy()
    {
        Destroy(headDriver?.gameObject);
        Destroy(spineDriver?.gameObject);
        Destroy(leftEyeDriver?.gameObject);
        Destroy(rightEyeDriver?.gameObject);
        Destroy(eyeCenter?.gameObject);
        Destroy(vrmLookAtTarget?.gameObject);
        //
        Destroy(upperArmRDriver?.gameObject);
        Destroy(upperArmLDriver?.gameObject);
        Destroy(rightUpperArmBone?.gameObject);
        Destroy(leftUpperArmBone?.gameObject);
    }
}
