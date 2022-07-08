﻿using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using System;

public static class BVHUtils
    {
    // BVH to Unity

    public static Quaternion fromEulerZXY(Vector3 euler)
    {
        return Quaternion.AngleAxis(euler.z, Vector3.forward) * Quaternion.AngleAxis(euler.y, Vector3.up) * Quaternion.AngleAxis(euler.x, Vector3.right);
    }

    public static float wrapAngle(float a)
    {
        if (a > 180f)
        {
            return a - 360f;
        }
        if (a < -180f)
        {
            return 360f + a;
        }
        return a;
    }

    public static void printDictionary<T1, T2>(Dictionary<T1, T2> dict)
    {
        foreach (KeyValuePair<T1, T2> kvp in dict)
        {
            Debug.Log(string.Format("Key = {0}, Value = {1}", kvp.Key, kvp.Value));
        }
    }

    enum Channel
    {
        XPos,
        YPos,
        ZPos,
        XRot,
        YRot,
        ZRot
    }

    // 0 = Xpos, 1 = Ypos, 2 = Zpos, 3 = Xrot, 4 = Yrot, 5 = Zrot
    private static float getAtFrame(BVHParser.BVHBone bone, Channel c, int frame)
    {
        switch (c)
        {
            case Channel.XPos:
                return bone.channels[0].values[frame] * .01f;
            case Channel.YPos:
                return bone.channels[1].values[frame] * .01f;
            case Channel.ZPos:
                return bone.channels[2].values[frame] * .01f;
            case Channel.XRot:
                return bone.channels[3].values[frame];
            case Channel.YRot:
                return bone.channels[4].values[frame];
            case Channel.ZRot:
                return bone.channels[5].values[frame];
        }
        throw new InvalidOperationException("getAtCurrentFrame called with invalid params: " + bone.ToString() + " , Channel: " + c);
    }

    private static Vector3 getDifferenceInPosition(BVHParser.BVHBone bone, int frame)
    {
        float xPos = -(getAtFrame(bone, Channel.XPos, frame) - getAtFrame(bone, Channel.XPos, frame - 1));
        float yPos = 0;// getAtFrame(bone, Channel.YPos, frame) - getAtFrame(bone, Channel.YPos, frame - 1);
        float zPos = getAtFrame(bone, Channel.ZPos, frame) - getAtFrame(bone, Channel.ZPos, frame - 1);
        return new Vector3(xPos, yPos, zPos);
    }
    public static void playFrame(int frame, List<BVHParser.BVHBone> boneList, Dictionary<string, Transform> nameToTransformMap, bool blender =true, bool applyMotion =true)
    {
        //Debug.Log("Playing frame: " + currentFrame);
        bool first = false;
        foreach (BVHParser.BVHBone bone in boneList)
        {
            Transform curTransform = nameToTransformMap[bone.name];
            first = bone.channels[0].enabled;
            // cheating here - we know that only hips will have pos data
            if (applyMotion && first && frame > 0) // update position
            {
                if (blender)
                {
                    curTransform.position += getDifferenceInPosition(bone, frame);
                }
                else
                {
                    Vector3 bonePos = new Vector3(-getAtFrame(bone, Channel.XPos, frame), getAtFrame(bone, Channel.YPos, frame), getAtFrame(bone, Channel.ZPos, frame));
                    Vector3 bvhPosition = curTransform.parent.InverseTransformPoint(bonePos + curTransform.parent.position);
                    curTransform.localPosition = bvhPosition;
                }

            }
            // Update rotation
            float xRot = wrapAngle(getAtFrame(bone, Channel.XRot, frame));
            float yRot = wrapAngle(getAtFrame(bone, Channel.YRot, frame));
            float zRot = wrapAngle(getAtFrame(bone, Channel.ZRot, frame));
            Vector3 eulerBVH = new Vector3(xRot, yRot, zRot);
            Quaternion rot = fromEulerZXY(eulerBVH);
            curTransform.localRotation = new Quaternion(rot.x, -rot.y, -rot.z, rot.w);
        }

    }

    // This function doesn't call any Unity API functions and should be safe to call from another thread
    public static BVHParser parseFile(string filename, int overrideFrames = -1)
    {
        BVHParser bp = new BVHParser(File.ReadAllText(Application.dataPath + "/LAFLAN Data/Animations/" + filename), overrideFrames);
        return bp;
    }

    // Names in BVH file for bones and model gameobject names need to match in order for this approach to work
    public static Dictionary<string, Transform> loadTransforms(Transform transform)
    {
        Dictionary<string, Transform> nameToTransformMap = new Dictionary<string, Transform>();
        Queue<Transform> transforms = new Queue<Transform>();

        transforms.Enqueue(transform);
        while (transforms.Any())
        {
            Transform curTransform = transforms.Dequeue();
            nameToTransformMap.Add(curTransform.name, curTransform);
            for (int i = 0; i < curTransform.childCount; i++)
            {
                transforms.Enqueue(curTransform.GetChild(i));
            }
        }
        return nameToTransformMap;
    }

    public static void lerp(int a_frameIdx, List<BVHParser.BVHBone> a_boneList, int b_frameIdx, List<BVHParser.BVHBone> b_boneList, Dictionary<string, Transform> nameToTransformMap, float transTime, bool blender, bool applyMotion)
    {
        bool first = false;
        for (int i = 0; i < a_boneList.Count; i++)
        {
            BVHParser.BVHBone a_bone = a_boneList[i];
            BVHParser.BVHBone b_bone = b_boneList[i];
            Transform curTransform = nameToTransformMap[a_bone.name];
            if (a_bone.name != b_bone.name)
            {
                throw new Exception("NAMES DON'T MATCH! : " + a_bone.name + " : " + b_bone.name);
            }
            first = a_bone.channels[0].enabled;
            if (applyMotion && first) // update position
            {
                if (blender)
                {
                    Vector3 a_translation = getDifferenceInPosition(a_bone, a_frameIdx);
                    Vector3 b_translation = getDifferenceInPosition(b_bone, b_frameIdx);
                    curTransform.position += Vector3.Slerp(a_translation, b_translation, transTime);
                }
                else
                {
                    //Vector3 bonePos = new Vector3(-getAtFrame(bone, Channel.XPos, frame), getAtFrame(bone, Channel.YPos, frame), getAtFrame(bone, Channel.ZPos, frame));
                    //Vector3 bvhPosition = curTransform.parent.InverseTransformPoint(bonePos + curTransform.parent.position);
                    //curTransform.localPosition = bvhPosition;
                }

            }
            // cheating here - we know that only hips will have pos data
            // Update rotation
            float xRot = wrapAngle(getAtFrame(a_bone, Channel.XRot, a_frameIdx));
            float yRot = wrapAngle(getAtFrame(a_bone, Channel.YRot, a_frameIdx));
            float zRot = wrapAngle(getAtFrame(a_bone, Channel.ZRot, a_frameIdx));
            Vector3 eulerBVH = new Vector3(xRot, yRot, zRot);
            Quaternion rot = fromEulerZXY(eulerBVH);
            Quaternion a_rot = new Quaternion(rot.x, -rot.y, -rot.z, rot.w);

            xRot = wrapAngle(getAtFrame(b_bone, Channel.XRot, b_frameIdx));
            yRot = wrapAngle(getAtFrame(b_bone, Channel.YRot, b_frameIdx));
            zRot = wrapAngle(getAtFrame(b_bone, Channel.ZRot, b_frameIdx));
            eulerBVH = new Vector3(xRot, yRot, zRot);
            rot = fromEulerZXY(eulerBVH);
            Quaternion b_rot = new Quaternion(rot.x, -rot.y, -rot.z, rot.w);
            curTransform.localRotation = Quaternion.Slerp(a_rot, b_rot, transTime);
        }
    }

    private static Vector3 getPositionDiffAtFrame(BVHParser.BVHBone bone, int startFrame, int endFrame)
    {
        float xPos = -getAtFrame(bone, Channel.XPos, startFrame);
        //float yPos =  getAtFrame(bone, Channel.YPos, startFrame);
        float zPos = getAtFrame(bone, Channel.ZPos, startFrame);
        float nextXPos = -getAtFrame(bone, Channel.XPos, endFrame);
        //float nextYPos =  getAtFrame(bone, Channel.YPos, endFrame) ;
        float nextZPos = getAtFrame(bone, Channel.ZPos, endFrame);
        return new Vector3(nextXPos - xPos, 0, nextZPos - zPos);
    }

    public static void getTrajectoryNFramesFromNow(List<BVHParser.BVHBone> bones, int startFrame, int frameNum , out float curAnimFutureXPos, out float curAnimFutureZPos)
    {
        BVHParser.BVHBone hipBone = bones[0];
        //if (hipBone.name != "hip")
        //{
        //    Debug.Log("Hip bone is actually : " + hipBone.name);
        //}
        Vector3 diff = getPositionDiffAtFrame(hipBone, startFrame, startFrame + frameNum);
        curAnimFutureXPos = diff.x;
        curAnimFutureZPos = diff.z;

    }
    public static void interializationBlend(BVHParser bp, Dictionary<BVHParser.BVHBone, Transform> boneToTransformMap, Transform transform, int frameIdx)
    {

    }

    public static void debugArray<T>(T[] data, string name)
    {
        Debug.Log(name + string.Join(",", data));
    }
}
