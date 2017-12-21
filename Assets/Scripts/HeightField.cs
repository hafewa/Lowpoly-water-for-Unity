﻿using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class HeightField : MonoBehaviour
{
    public int width;                       ///  width of height field
    public int depth;                       ///  depth of height field

    public float speed;                     ///  speed of waves
    //private float size;                     //  grid spacing

    public float quadSize;                  ///  size of one quad
    public float maxHeight;                 ///  maximum height in height field
    public float maxVelocity;               ///  maximum velocity of vertices
    public float randVelocity;              ///  apply random velocity to randomly chosen vertices
    public float dampingVelocity;           ///  damping factor for velocities

    private float[] heights;               ///  store height values
    private float[] velocities;            ///  store velocities

    private Vector3[] newVertices;          ///  store vertices of mesh
    private int[] newTriangles;             ///  store triangles of mesh

    void Start()
    {
        //size = 1.2f;
        dampingVelocity = 1f;
        heights = new float[width * depth];
        velocities = new float[width * depth];
        newVertices = new Vector3[width * depth];
        newTriangles = new int[(width - 1) * (depth - 1) * 6];

        CreateMesh2();
    }

    void CreateMesh()
    {
        Vector2[] newUV;
        newUV = new Vector2[newVertices.Length];

        //  initialize vertices positions
        heights[(int)(width / 2f * depth + depth / 2f)] = maxHeight;
        heights[(int)((width / 2f + 1) * depth + depth / 2f + 1)] = maxHeight;
        heights[(int)((width / 2f + 1) * depth + depth / 2f)] = maxHeight;
        heights[(int)(width / 2f * depth + depth / 2f + 1)] = maxHeight;
        heights[(int)((width / 2f + 1) * depth + depth / 2f - 1)] = maxHeight;
        heights[(int)((width / 2f - 1) * depth + depth / 2f + 1)] = maxHeight;
        heights[(int)((width / 2f - 1) * depth + depth / 2f - 1)] = maxHeight;
        heights[(int)((width / 2f - 1) * depth + depth / 2f)] = maxHeight;
        heights[(int)(width / 2f * depth + depth / 2f - 1)] = maxHeight;

        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < depth; j++)
            {
                velocities[i * depth + j] = 0;
                //if (i == 0)
                //   heights[i * depth + j] += 10.0;
                newVertices[i * depth + j] = new Vector3(i * quadSize, heights[i * depth + j], j * quadSize);
            }
        }

        //  initialize texture coordinates
        for (int i = 0; i < newUV.Length; i++)
        {
            newUV[i] = new Vector2(newVertices[i].x, newVertices[i].z);
        }

        //  represent quads by two triangles
        int tri = 0;
        for (int i = 0; i < width - 1; i++)
        {
            for (int j = 0; j < depth - 1; j++)
            {
                newTriangles[tri + 2] = (i + 1) * depth + (j + 1);
                newTriangles[tri + 1] = i * depth + (j + 1);
                newTriangles[tri] = i * depth + j;
                tri += 3;

                newTriangles[tri + 2] = (i + 1) * depth + j;
                newTriangles[tri + 1] = (i + 1) * depth + (j + 1);
                newTriangles[tri] = i * depth + j;
                tri += 3;
            }
        }
        
        Mesh mesh;
        //  create new mesh
        mesh = new Mesh();

        mesh.vertices = newVertices;
        mesh.triangles = newTriangles;
        mesh.uv = newUV;
        //mesh.RecalculateNormals();

        GetComponent<MeshFilter>().mesh = mesh;
    }

    void CreateMesh2()
    {
        Vector2[] newUV;
        newUV = new Vector2[newVertices.Length];

        heights[(int)(width / 2f * depth + depth / 2f)] = maxHeight;
        heights[(int)((width / 2f + 1) * depth + depth / 2f + 1)] = maxHeight;
        heights[(int)((width / 2f + 1) * depth + depth / 2f)] = maxHeight;
        heights[(int)(width / 2f * depth + depth / 2f + 1)] = maxHeight;
        heights[(int)((width / 2f + 1) * depth + depth / 2f - 1)] = maxHeight;
        heights[(int)((width / 2f - 1) * depth + depth / 2f + 1)] = maxHeight;
        heights[(int)((width / 2f - 1) * depth + depth / 2f - 1)] = maxHeight;
        heights[(int)((width / 2f - 1) * depth + depth / 2f)] = maxHeight;
        heights[(int)(width / 2f * depth + depth / 2f - 1)] = maxHeight;

        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < depth; j++)
            {
                velocities[i * depth + j] = 0;
                if (i != 0 && j != 0 && i != width - 1 && j != depth - 1)
                    newVertices[i * depth + j] = new Vector3(i * quadSize + Random.Range(-quadSize / 2.1f, quadSize / 2.1f), heights[i * depth + j], j * quadSize + Random.Range(-quadSize / 2.1f, quadSize / 2.1f));
                else
                    newVertices[i * depth + j] = new Vector3(i * quadSize, heights[i * depth + j], j * quadSize);
            }
        }
        //  initialize texture coordinates
        for (int i = 0; i < newUV.Length; i++)
        {
            newUV[i] = new Vector2(newVertices[i].x, newVertices[i].z);
        }

        //  represent quads by two triangles
        int tri = 0;
        for (int i = 0; i < width - 1; i++)
        {
            for (int j = 0; j < depth - 1; j++)
            {
                newTriangles[tri + 2] = (i + 1) * depth + (j + 1);
                newTriangles[tri + 1] = i * depth + (j + 1);
                newTriangles[tri] = i * depth + j;
                tri += 3;

                newTriangles[tri + 2] = (i + 1) * depth + j;
                newTriangles[tri + 1] = (i + 1) * depth + (j + 1);
                newTriangles[tri] = i * depth + j;
                tri += 3;
            }
        }
        
        Mesh mesh;
        //  create new mesh
        mesh = new Mesh();

        mesh.vertices = newVertices;
        mesh.triangles = newTriangles;
        mesh.uv = newUV;
        mesh.RecalculateNormals();

        GetComponent<MeshFilter>().mesh = mesh;
    }

    void Update()
    {
        //  update velocities for all vertices
        int sqrt = (int)Mathf.Sqrt(width * depth);
        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < depth; j++)
            {
                velocities[i * depth + j] += Time.deltaTime * speed * speed * ((heights[Mathf.Max(i - 1, 0) * depth + j] + heights[Mathf.Min(width - 1, i + 1) * depth + j]
                    + heights[i * depth + Mathf.Max(j - 1, 0)] + heights[i * depth + Mathf.Min(depth - 1, j + 1)]) - 4 * heights[i * depth + j]);
                // (size * size);

                if (Random.Range(0, sqrt) == 0)
                {
                    velocities[i * depth + j] += Random.Range(-randVelocity, randVelocity);
                }
                velocities[i * depth + j] = Mathf.Clamp(velocities[i * depth + j], -maxVelocity, maxVelocity);
                velocities[i * depth + j] *= dampingVelocity;
            }
        }

        //  update positions 
        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < depth; j++)
            {
                heights[i * depth + j] += velocities[i * depth + j] * Time.deltaTime;
                heights[i * depth + j] = Mathf.Clamp(heights[i * depth + j], -maxHeight, maxHeight);
                if (i != 0 && j != 0 && i != width - 1 && j != depth - 1)
                {
                    Vector3 pos = newVertices[i * depth + j];
                    int k, m = 0;
                    k = (int)(pos.x / quadSize);
                    m = (int)(pos.z / quadSize);
                    float x1 = heights[k * depth + m];
                    float x2 = heights[(k + 1) * depth + m + 1];
                    float x3 = heights[k * depth + m + 1];
                    float x4 = heights[(k + 1) * depth + m];
                    float x = (pos.x / quadSize - k);
                    float y = (pos.z / quadSize - m);
                    float res = (x1 * x + x4 * (1 - x)) * y + (x3 * x + x2 * (1 - x)) * (1 - y);
                    newVertices[i * depth + j] = new Vector3(pos.x, res, pos.z);
                }
                else
                {
                    Vector3 pos = newVertices[i * depth + j];

                    newVertices[i * depth + j] = new Vector3(pos.x, heights[i * depth + j], pos.z);
                }
            }
        }
        
        Mesh mesh = GetComponent<MeshFilter>().mesh;
        //  set mesh again
        mesh.Clear();

        mesh.vertices = newVertices;
        mesh.triangles = newTriangles;
        mesh.RecalculateNormals();

        GetComponent<MeshFilter>().mesh = mesh;
    }

    public void StartWave()
    {
        for (int i = 0; i < width; i++)
        {
            heights[i * depth] = maxHeight;
        }
    }
}
