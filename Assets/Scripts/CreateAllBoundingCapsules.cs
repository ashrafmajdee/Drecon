using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CreateAllBoundingCapsules : MonoBehaviour
{
    public GameObject[] boneObjects;
    // maps idx -> corresponding bone ID 
    public int[] bone_ids;
    public int left_hand_bone_id = 9;
    public int right_hand_bone_id = 36;
    public int num_bones = 70;
    private HashSet<int> left_hand_ids;
    private HashSet<int> right_hand_ids;
    private int left_hand_ids_start = 10;
    private int left_hand_ids_end = 32;
    private int right_hand_ids_start = 37;
    private int right_hand_ids_end = 59;

    private int right_foot_ids_start = 62;
    private int right_foot_ids_end = 64;
    private int left_foot_ids_start = 67;
    private int left_foot_ids_end = 69;
    // bone_verts_lists[i] = a list of vertices associated with bone i
    private List<Vector3>[] bone_verts_lists;
    public bool debug;
    public int[] debug_bone_ids;
    public float min_rad = .005f;
    Vector3[] m_vertices;

    [ContextMenu("Build all bone capsulse")]
    private void buildBoneCapsules()
    {
        debug_lines = new List<Vector3[]>();
        debug_points = new List<Vector3>();
        getBoneVerts();

        for (int i = 0; i < bone_ids.Length; i++)
        {
            if (debug && !debug_bone_ids.Contains(bone_ids[i]))
                continue;
            GameObject boneObject = boneObjects[i];
            bool[] trash = new bool[0];
            Vector3[] verts = filterVertices(bone_verts_lists[bone_ids[i]].ToArray(), min_rad, ref trash);
            debug_points.AddRange(verts);
            GameObject capsuleObject = calculateBoundingCapsule(verts, bone_ids[i]);
            capsuleObject.transform.parent = boneObject.transform;
        }
    }

    public int[] bone_ids_use_small_rad;

    private List<Vector3[]> debug_lines;
    private List<Vector3> debug_points;

    private GameObject calculateBoundingCapsule(Vector3[] verts, int bone_id)
    {

        int n = verts.Length;
        Vector3 mean = GeoUtils.calculateMean(verts);
        double[,] covar = GeoUtils.calculateCovarMat(verts);
        double[] eigenvalues = GeoUtils.getEigenvalues(covar);
        Vector3 largest_eigen = GeoUtils.getEigenvectorFromValue(covar, eigenvalues[0]).normalized;
        Vector3[] proj_verts = GeoUtils.projectVertsOntoAxis(verts, mean, mean + largest_eigen);
        //debug_points.AddRange(proj_verts);
        Vector3 center = Vector3.zero;
        float height = GeoUtils.getMaxDistApart(proj_verts, ref center);
        float radius;
        if (bone_ids_use_small_rad.Contains(bone_id))
        {
            double dist_from_main_axis_sum = 0;
            foreach (Vector3 v in verts)
                dist_from_main_axis_sum += (v - GeoUtils.closestPointOnLine(mean, mean + largest_eigen, v)).magnitude;
            radius = (float)dist_from_main_axis_sum / n;
        }
        else
        {
            double max_dist_from_main_axis = 0;
            foreach (Vector3 v in verts)
                max_dist_from_main_axis = Math.Max(max_dist_from_main_axis, (v - GeoUtils.closestPointOnLine(mean, mean + largest_eigen, v)).magnitude);
            radius = (float)max_dist_from_main_axis;
        }
        //height += 2 * radius;

        debug_lines.Add(new Vector3[] { mean - largest_eigen, mean + largest_eigen });
        GameObject capsuleObject = new GameObject();
        capsuleObject.transform.position = center;
        capsuleObject.transform.rotation = GeoUtils.getRotationBetween(Vector3.right, largest_eigen);
        CapsuleCollider capsule = capsuleObject.AddComponent<CapsuleCollider>();
        capsule.height = height;
        capsule.direction = 0;
        capsule.radius = radius;
        return capsuleObject;
    }
    public bool draw_gizmos;
    private void OnDrawGizmos()
    {
        if (!draw_gizmos || debug_lines == null || debug_points == null) 
            return;
        Gizmos.color = Color.blue;
        foreach(Vector3[] line in debug_lines)
        {
            Gizmos.DrawLine(line[0], line[1]);
        }
        Gizmos.color = Color.green;
        foreach (Vector3 v in debug_points)
            Gizmos.DrawSphere(v, .005f);
    }

    // Rudimentary algorithm to filter out a bunch of verts that are close to each other
    private Vector3[] filterVertices(Vector3[] verts, float min_rad, ref bool[] kept)
    {
        int n = verts.Length;
        bool[] keep = Enumerable.Repeat(true, n).ToArray();
        int it = 0;
        while (true)
        {
            bool should_break = true;
            for(int i = 0; i < n; i++)
            {
                if (!keep[i])
                    continue;
                for (int j = 0; j < n; j++)
                {
                    if (!keep[j] || i == j)
                        continue;
                    if (Vector3.Distance(verts[i], verts[j]) < min_rad)
                    {
                        keep[i] = false;
                        should_break = false;
                        break;
                    }
                }
            }
            if (should_break)
                break;
            it++;
        }
        List<Vector3> new_verts = new List<Vector3>();
        for (int i = 0; i < n; i++)
            if (keep[i])
                new_verts.Add(verts[i]);
        Debug.Log($"Original Length: {n} New verts length: {new_verts.Count} Iterations: {it}");
        kept = keep;
        return new_verts.ToArray();
    }
    private void getBoneVerts()
    {
        bone_verts_lists = new List<Vector3>[num_bones];
        for (int i = 0; i < 70; i++)
            bone_verts_lists[i] = new List<Vector3>();
        left_hand_ids = new HashSet<int>();
        right_hand_ids = new HashSet<int>();

        for (int i = left_hand_ids_start; i < left_hand_ids_end + 1; i++)
            left_hand_ids.Add(i);
        for (int i = right_hand_ids_start; i < right_hand_ids_end + 1; i++)
            right_hand_ids.Add(i);
        SkinnedMeshRenderer rend = GetComponent<SkinnedMeshRenderer>();

        Mesh mesh = rend.sharedMesh;
        BoneWeight[] bws = mesh.boneWeights;
        bool[] kept = new bool[mesh.vertexCount];
        m_vertices = mesh.vertices;
        filterVertices(mesh.vertices, min_rad, ref kept);
        for (int i = 0; i < mesh.vertexCount; i++)
        {
            if (!kept[i])
                continue;
            BoneWeight bw = bws[i];
            int bone_id = -1;
            if (bw.weight0 > Mathf.Max(bw.weight1, bw.weight2, bw.weight3))
                bone_id = bw.boneIndex0;
            else if (bw.weight1 > Mathf.Max(bw.weight0, bw.weight2, bw.weight3))
                bone_id = bw.boneIndex1;
            else if (bw.weight2 > Mathf.Max(bw.weight1, bw.weight0, bw.weight3))
                bone_id = bw.boneIndex2;
            else if (bw.weight3 > Mathf.Max(bw.weight1, bw.weight2, bw.weight0))
                bone_id = bw.boneIndex3;
            if (bone_id == -1)
                continue;

            if (left_hand_ids.Contains(bone_id))
                bone_id = left_hand_bone_id;
            else if (right_hand_ids.Contains(bone_id))
                bone_id = right_hand_bone_id;
            else if (bone_id >= right_foot_ids_start && bone_id <= right_foot_ids_end)
                bone_id = right_foot_ids_start;
            else if (bone_id >= left_foot_ids_start && bone_id <= left_foot_ids_end)
                bone_id = left_foot_ids_start;
            bone_verts_lists[bone_id].Add(m_vertices[i]);
        }
    }
}
