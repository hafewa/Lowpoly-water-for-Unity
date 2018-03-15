﻿using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

//[ExecuteInEditMode]
public class HeightField : MonoBehaviour
{
    public struct heightField
    {
        public float height;
        public float velocity;
    }

    struct int2
    {
        public int x;
        public int y;
    }

    public enum WaterMode
    {
        Minimal, Reflection, Obstacles, ReflAndObstcl
    };

    //  public variables

    /// <summary>
    /// 0: simple water 
    /// 1: reflections 
    /// 2: obstacles reflect waves in realtime 
    /// 3: reflections + obstacles 
    /// </summary>       
    public WaterMode waterMode;

    /// <summary>
    /// Compute Shader for heightField updates
    /// </summary>
    public ComputeShader heightFieldCS;
    /// <summary>
    /// Main camera of the scene
    /// </summary>
    public Camera mainCam;

    /// <summary>
    /// The maximum random displacement of the vertices of the generated mesh
    /// </summary>
    [Range(0.0f, 1.0f)]
    public float maxRandomDisplacement;

    /// <summary>
    /// Width of the generated mesh
    /// </summary>
    [Range(8, 254)]
    public int width;
    /// <summary>
    /// Depth of the generated mesh
    /// </summary>
    [Range(8, 254)]
    public int depth;
    /// <summary>
    /// Distance between vertices of the generated mesh
    /// </summary>
    public float quadSize;

    /// <summary>
    /// Speed of waves
    /// </summary>
    public float speed;
    /// <summary>
    /// Also controls the speed of waves/updates
    /// </summary>
    public float gridSpacing;
    /// <summary>
    /// Maximum height values at the vertices
    /// </summary>       
    public float maxHeight;
    /// <summary>
    /// Maximum velocity values at the vertices
    /// </summary>       
    public float maxVelocity;
    /// <summary>
    /// Random inital velocity values at the vertices
    /// </summary>       
    public float randomInitialVelocity;
    /// <summary>
    /// Damping factor to reduce artifacts
    /// </summary>       
    public float dampingVelocity;

    /// <summary>
    /// Texture size of the reflection
    /// </summary>       
    public int textureSize = 256;
    /// <summary>
    /// Reflection plane offset
    /// </summary>       
    public float clipPlaneOffset = 0.07f;
    /// <summary>
    /// Layermask to ignore certain layers
    /// </summary>       
    public LayerMask reflectLayers = -1;
    /// <summary>
    /// chooses updatemode for the heightField
    /// </summary>    
    public bool useLinearEquation;

    //  private variables
    private ComputeBuffer heightFieldCB;
    private ComputeBuffer reflectWavesCB;
    private ComputeBuffer heightFieldCBOut;
    private ComputeBuffer verticesCB;

    private Vector2[] randomDisplacement;
    private float lastMaxRandomDisplacement;


    //  HEIGHTFIELD
    private heightField[] hf;
    private uint[] environment;
    private int kernel;                     ///   kernel for computeshader
    private int kernelVertices;

    private Dictionary<Camera, Camera> m_ReflectionCameras = new Dictionary<Camera, Camera>(); // Camera -> Camera table
    private RenderTexture reflectionTex;

    private int m_OldReflectionTextureSize;
    private Mesh planeMesh;
    private Vector3[] vertices;

    private uint currentCollision;

    //  SWE
    private Vector3[] U;
    private Vector3[] G;
    private Vector3[] F;
    private float[] B;

    private float dx;
    private float dy;

    public float frictionSWE;

    private int kernelSWE;
    private int kernelSWEFlux;
    private int kernelSWEBC;
    private int kernelSWEVertices;

    public float epsilon;
    
    private void Start()
    {
        Initialize();
    }

    void Initialize()
    {
        mainCam.depthTextureMode = DepthTextureMode.Depth;

        CreatePlaneMesh();
        initHeightField();
        setRandomDisplacementBuffer();
        CreateMesh();
        initBuffers();

        initValuesSWE();
        initBuffersSWE();

        currentCollision = 1;
    }

    void OnApplicationQuit()
    {
        heightFieldCB.Release();
        reflectWavesCB.Release();
        heightFieldCBOut.Release();
        verticesCB.Release();
    }

    void Update()
    {
        //  if noisy factor changes -> initialize randomDisplacements again
        if (!Mathf.Approximately(maxRandomDisplacement, lastMaxRandomDisplacement))
        {
            setRandomDisplacementBuffer();
        }
    }
    
    public void OnWillRenderObject()
    {
        if (useLinearEquation)
        {
            updateHeightfield();
            updateVertices();
        }
        else
            updateHeightVelocitySWE();
        if (waterMode == WaterMode.ReflAndObstcl || waterMode == WaterMode.Reflection)
        {
            Mesh oldMesh = GetComponent<MeshFilter>().mesh;
            GetComponent<MeshFilter>().mesh = planeMesh;
            if (!enabled || !GetComponent<Renderer>() || !GetComponent<Renderer>().sharedMaterial ||
                !GetComponent<Renderer>().enabled)
            {
                return;
            }

            Camera cam = Camera.current;
            if (!cam)
            {
                return;
            }

            Camera reflectionCamera;
            CreateWaterObjects(cam, out reflectionCamera);

            // find out the reflection plane: position and normal in world space
            Vector3 pos = transform.position;
            Vector3 normal = transform.up;

            UpdateCameraModes(cam, reflectionCamera);

            // Reflect camera around reflection plane
            float d = -Vector3.Dot(normal, pos) - clipPlaneOffset;
            Vector4 reflectionPlane = new Vector4(normal.x, normal.y, normal.z, d);

            Matrix4x4 reflection = Matrix4x4.zero;
            CalculateReflectionMatrix(ref reflection, reflectionPlane);
            Vector3 oldpos = cam.transform.position;
            Vector3 newpos = reflection.MultiplyPoint(oldpos);
            reflectionCamera.worldToCameraMatrix = cam.worldToCameraMatrix * reflection;

            // Setup oblique projection matrix so that near plane is our reflection
            // plane. This way we clip everything below/above it for free.
            Vector4 clipPlane = CameraSpacePlane(reflectionCamera, pos, normal, 1.0f);
            reflectionCamera.projectionMatrix = cam.CalculateObliqueMatrix(clipPlane);

            // Set custom culling matrix from the current camera
            reflectionCamera.cullingMatrix = cam.projectionMatrix * cam.worldToCameraMatrix;

            reflectionCamera.cullingMask = ~(1 << 4) & reflectLayers.value; // never render water layer
            reflectionCamera.targetTexture = reflectionTex;
            bool oldCulling = GL.invertCulling;
            GL.invertCulling = !oldCulling;
            reflectionCamera.transform.position = newpos;
            Vector3 euler = cam.transform.eulerAngles;
            reflectionCamera.transform.eulerAngles = new Vector3(-euler.x, euler.y, euler.z);
            reflectionCamera.Render();
            reflectionCamera.transform.position = oldpos;
            GL.invertCulling = oldCulling;
            GetComponent<Renderer>().sharedMaterial.SetTexture("_ReflectionTex", reflectionTex);
            GetComponent<MeshFilter>().mesh = oldMesh;
        }
    }

    void OnDisable()
    {
        foreach (var kvp in m_ReflectionCameras)
        {
            DestroyImmediate((kvp.Value).gameObject);
        }
        m_ReflectionCameras.Clear();
    }

    public void OnCollisionStay(Collision collision)
    {
        if (useLinearEquation)
        {
            if (waterMode == WaterMode.ReflAndObstcl || waterMode == WaterMode.Obstacles)
            {
                //environment = new uint[width * depth];
                //  temporary indices (collision points)
                int2[] tempIndices = new int2[collision.contacts.Length];
                for (int i = 0; i < collision.contacts.Length; i++)
                {
                    Vector3 coll = collision.contacts[i].point - transform.position;
                    int x = Math.Min(Math.Max(Mathf.RoundToInt(coll.x / quadSize), 0), width - 1);
                    int z = Math.Min(Math.Max(Mathf.RoundToInt(coll.z / quadSize), 0), depth - 1);
                    //if (hf[x * depth + z].height + maxHeight > coll.y)
                    environment[x * depth + z] = currentCollision;
                    tempIndices[i].x = x;
                    tempIndices[i].y = z;
                }
                //  fill contact points to represent mesh (for reflecting waves)
                for (int i = 0; i < tempIndices.Length; i++)
                {
                    int kTemp = tempIndices[i].x;
                    for (int k = kTemp; k < width; k++)
                    {
                        if (environment[k * depth + tempIndices[i].y] == currentCollision)
                        {
                            kTemp = k;
                        }
                    }
                    for (int n = tempIndices[i].x + 1; n < kTemp; n++)
                        environment[n * depth + tempIndices[i].y] = currentCollision;

                    kTemp = tempIndices[i].x;
                    for (int k = kTemp; k >= 0; k--)
                    {
                        if (environment[k * depth + tempIndices[i].y] == currentCollision)
                        {
                            kTemp = k;
                        }
                    }
                    for (int n = tempIndices[i].x - 1; n >= kTemp; n--)
                        environment[n * depth + tempIndices[i].y] = currentCollision;

                    kTemp = tempIndices[i].y;
                    for (int k = kTemp; k < depth; k++)
                    {
                        if (environment[tempIndices[i].x * depth + k] == currentCollision)
                        {
                            kTemp = k;
                        }
                    }
                    for (int n = tempIndices[i].y + 1; n < kTemp; n++)
                        environment[tempIndices[i].x * depth + n] = currentCollision;

                    kTemp = tempIndices[i].y;
                    for (int k = kTemp; k >= 0; k--)
                    {
                        if (environment[tempIndices[i].x * depth + k] == currentCollision)
                        {
                            kTemp = k;
                        }
                    }
                    for (int n = tempIndices[i].y - 1; n >= kTemp; n--)
                        environment[tempIndices[i].x * depth + n] = currentCollision;
                }
                reflectWavesCB.SetData(environment);
                currentCollision = (currentCollision + 1) % int.MaxValue;
            }
        }
    }

    private void setRandomDisplacementBuffer()
    {
        randomDisplacement = new Vector2[width * depth];
        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < depth; j++)
            {
                if (i != 0 && j != 0 && i != width - 1 && j != depth - 1)
                    randomDisplacement[i * depth + j] = new Vector2(UnityEngine.Random.Range(-maxRandomDisplacement * quadSize / 3.0f, maxRandomDisplacement * quadSize / 3.0f),
                    UnityEngine.Random.Range(-maxRandomDisplacement * quadSize / 3.0f, maxRandomDisplacement * quadSize / 3.0f));
            }
        }
        lastMaxRandomDisplacement = maxRandomDisplacement;
    }

    private void initHeightField()
    {
        hf = new heightField[width * depth];

        hf[(int)(width / 2f * depth + depth / 2f)].height = maxHeight;
        hf[(int)((width / 2f + 1) * depth + depth / 2f + 1)].height = maxHeight;
        hf[(int)((width / 2f + 1) * depth + depth / 2f)].height = maxHeight;
        hf[(int)(width / 2f * depth + depth / 2f + 1)].height = maxHeight;
        hf[(int)((width / 2f + 1) * depth + depth / 2f - 1)].height = maxHeight;
        hf[(int)((width / 2f - 1) * depth + depth / 2f + 1)].height = maxHeight;
        hf[(int)((width / 2f - 1) * depth + depth / 2f - 1)].height = maxHeight;
        hf[(int)((width / 2f - 1) * depth + depth / 2f)].height = maxHeight;
        hf[(int)(width / 2f * depth + depth / 2f - 1)].height = maxHeight;

        for (int i = 0; i < hf.Length; i++)
        {
            hf[i].velocity += UnityEngine.Random.Range(-randomInitialVelocity, randomInitialVelocity);
        }
    }

    private void initBuffers()
    {
        //  initialize buffers
        heightFieldCB = new ComputeBuffer(width * depth, 8);
        heightFieldCBOut = new ComputeBuffer(width * depth, 8);
        reflectWavesCB = new ComputeBuffer(width * depth, 4);
        verticesCB = new ComputeBuffer(width * depth, 12);
        environment = new uint[width * depth];

        heightFieldCB.SetData(hf);
        reflectWavesCB.SetData(environment);

        //  get corresponding kernel index
        kernel = heightFieldCS.FindKernel("updateHeightfield");
        kernelVertices = heightFieldCS.FindKernel("interpolateVertices");
        //  set constants
        heightFieldCS.SetFloat("g_fQuadSize", quadSize);
        heightFieldCS.SetInt("g_iDepth", depth);
        heightFieldCS.SetInt("g_iWidth", width);
        heightFieldCS.SetFloat("g_fGridSpacing", gridSpacing); // could be changed to quadSize, but does not yield good results

        Shader.SetGlobalFloat("g_fQuadSize", quadSize);
        Shader.SetGlobalInt("g_iDepth", depth);
        Shader.SetGlobalInt("g_iWidth", width);
    }

    //  dispatch of compute shader
    private void updateHeightfield()
    {
        //  calculate average of all points in the heightfield (might be unecessary)
        float currentAvgHeight = 0.0f;
        int length = Math.Min(hf.Length, 512);
        for (int i = 0; i < length; i++)
        {
            currentAvgHeight += hf[i].height;
        }
        currentAvgHeight /= length;
        clipPlaneOffset = currentAvgHeight;

        heightFieldCS.SetBuffer(kernel, "heightFieldIn", heightFieldCB);
        heightFieldCS.SetBuffer(kernel, "reflectWaves", reflectWavesCB);
        heightFieldCS.SetBuffer(kernel, "heightFieldOut", heightFieldCBOut);

        heightFieldCS.SetFloat("g_fDeltaTime", Time.deltaTime);
        heightFieldCS.SetFloat("g_fSpeed", speed);
        heightFieldCS.SetFloat("g_fMaxVelocity", maxVelocity);
        heightFieldCS.SetFloat("g_fMaxHeight", maxHeight);
        heightFieldCS.SetFloat("g_fDamping", dampingVelocity);
        heightFieldCS.SetFloat("g_fAvgHeight", currentAvgHeight);
        heightFieldCS.SetFloat("g_fGridSpacing", gridSpacing);

        heightFieldCS.Dispatch(kernel, Mathf.CeilToInt(width / 16.0f), Mathf.CeilToInt(depth / 16.0f), 1);
        heightFieldCBOut.GetData(hf);
        heightFieldCB.SetData(hf);
        Shader.SetGlobalBuffer("g_HeightField", heightFieldCBOut);
        environment = new uint[width * depth];
    }

    private void updateVertices()
    {
        Mesh mesh = GetComponent<MeshFilter>().mesh;
        Vector3[] verts = mesh.vertices;

        ComputeBuffer randomXZ = new ComputeBuffer(width * depth, 8);
        randomXZ.SetData(randomDisplacement);
        verticesCB.SetData(vertices);
        heightFieldCS.SetBuffer(kernelVertices, "heightFieldIn", heightFieldCB);
        heightFieldCS.SetBuffer(kernelVertices, "verticesPosition", verticesCB);
        heightFieldCS.SetBuffer(kernelVertices, "randomDisplacement", randomXZ);

        heightFieldCS.Dispatch(kernelVertices, Mathf.CeilToInt(verts.Length / 256), 1, 1);
        verticesCB.GetData(verts);

        mesh.vertices = verts;
        //mesh.RecalculateNormals();
        GetComponent<MeshFilter>().mesh = mesh;
        randomXZ.Release();
    }

    private void initValuesSWE()
    {
        U = new Vector3[width * depth];
        F = new Vector3[(width + 1) * (depth)];
        G = new Vector3[(width) * (depth + 1)];
        B = new float[width * depth];

        U[(int)(width / 2f * depth + depth / 2f)].x = maxHeight;
        U[(int)((width / 2f + 1) * depth + depth / 2f + 1)].x = maxHeight;
        U[(int)((width / 2f + 1) * depth + depth / 2f)].x = maxHeight;
        U[(int)(width / 2f * depth + depth / 2f + 1)].x = maxHeight;
        U[(int)((width / 2f + 1) * depth + depth / 2f - 1)].x = maxHeight;
        U[(int)((width / 2f - 1) * depth + depth / 2f + 1)].x = maxHeight;
        U[(int)((width / 2f - 1) * depth + depth / 2f - 1)].x = maxHeight;
        U[(int)((width / 2f - 1) * depth + depth / 2f)].x = maxHeight;
        U[(int)(width / 2f * depth + depth / 2f - 1)].x = maxHeight;

        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < depth; j++)
            {
                float x = (i - width / 2.0f) * quadSize;
                float y = (j - depth / 2.0f) * quadSize;
                if (Mathf.Sqrt(x * x + y * y) < Mathf.Sqrt(quadSize * width /4.0f) * Mathf.Min(quadSize, quadSize))
                    U[i * depth + j].x = maxHeight/2.0f;
                else
                    U[i * depth + j].x = 1.0f;
            }
        }
        for (int i = 0; i < B.Length; i++)
        {
            B[i] = 10;
        }
    }

    private void initBuffersSWE()
    {
        kernelSWE = heightFieldCS.FindKernel("updateHeightfieldUsingSWE");
        kernelSWEBC = heightFieldCS.FindKernel("applyBC");
        kernelSWEFlux = heightFieldCS.FindKernel("updateFlux");
        kernelSWEVertices = heightFieldCS.FindKernel("interpolateVerticesSWE");
    }

    private void updateHeightVelocitySWE()
    {
        ComputeBuffer U_read = new ComputeBuffer(U.Length, 12);
        ComputeBuffer U_RW = new ComputeBuffer(U.Length, 12);

        ComputeBuffer F_RW = new ComputeBuffer(F.Length, 12);
        ComputeBuffer G_RW = new ComputeBuffer(G.Length, 12);

        U_read.SetData(U);

        heightFieldCS.SetBuffer(kernelSWEFlux, "F_new", F_RW);
        heightFieldCS.SetBuffer(kernelSWEFlux, "G_new", G_RW);
        heightFieldCS.SetBuffer(kernelSWEFlux, "U_new", U_read);

        heightFieldCS.SetFloat("g_fGravity", Mathf.Abs(Physics.gravity.y));
        heightFieldCS.SetFloat("g_fGridSpacing", quadSize);
        heightFieldCS.SetFloat("g_fDeltaTime", Time.deltaTime);
        heightFieldCS.SetFloat("g_fManning", frictionSWE);

        heightFieldCS.Dispatch(kernelSWEFlux, Mathf.CeilToInt(width / 16.0f) + 1, Mathf.CeilToInt(depth / 16.0f) + 1, 1);

        heightFieldCS.SetBuffer(kernelSWE, "F", F_RW);
        heightFieldCS.SetBuffer(kernelSWE, "U", U_read);
        heightFieldCS.SetBuffer(kernelSWE, "G", G_RW);
        heightFieldCS.SetBuffer(kernelSWE, "U_new", U_RW);

        heightFieldCS.Dispatch(kernelSWE, Mathf.CeilToInt(width / 16.0f), Mathf.CeilToInt(depth / 16.0f), 1);

        heightFieldCS.SetBuffer(kernelSWEBC, "U_new", U_RW);

        heightFieldCS.Dispatch(kernelSWEBC, Mathf.CeilToInt(width / 16.0f), Mathf.CeilToInt(depth / 16.0f), 1);

        U_RW.GetData(U);
        F_RW.GetData(F);
        G_RW.GetData(G);

        float currentAvgHeight = 0.0f;
        int length = Math.Min(U.Length, 512);
        for (int i = 0; i < length; i++)
        {
            currentAvgHeight += U[i].x;
        }
        currentAvgHeight /= length;
        clipPlaneOffset = currentAvgHeight;

        Mesh mesh = GetComponent<MeshFilter>().mesh;
        Vector3[] verts = mesh.vertices;

        ComputeBuffer randomXZ = new ComputeBuffer(width * depth, 8);
        randomXZ.SetData(randomDisplacement);
        verticesCB.SetData(vertices);
        heightFieldCS.SetBuffer(kernelSWEVertices, "U", U_RW);
        heightFieldCS.SetBuffer(kernelSWEVertices, "verticesPosition", verticesCB);
        heightFieldCS.SetBuffer(kernelSWEVertices, "randomDisplacement", randomXZ);

        heightFieldCS.Dispatch(kernelSWEVertices, Mathf.CeilToInt(verts.Length / 256), 1, 1);
        verticesCB.GetData(verts);

        mesh.vertices = verts;
        GetComponent<MeshFilter>().mesh = mesh;

        U_read.Release();
        F_RW.Release();
        G_RW.Release();
        U_RW.Release();
        randomXZ.Release();
    }

    //  creates mesh with flat shading
    private void CreateMesh()
    {
        Vector2[] newUV;
        Vector3[] newVertices;
        int[] newTriangles;

        newVertices = new Vector3[width * depth];
        newTriangles = new int[(width - 1) * (depth - 1) * 6];
        newUV = new Vector2[newVertices.Length];

        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < depth; j++)
            {
                newVertices[i * depth + j] = new Vector3(i * quadSize, 0.0f, j * quadSize);
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
        //  create new mesh
        Mesh mesh = new Mesh();

        mesh.MarkDynamic();
        mesh.vertices = newVertices;
        mesh.triangles = newTriangles;
        mesh.uv = newUV;
        vertices = newVertices;
        
        GetComponent<MeshFilter>().mesh = mesh;
        GetComponent<BoxCollider>().size = new Vector3(quadSize * width, maxHeight / 2.0f, quadSize * depth);
        GetComponent<BoxCollider>().center = new Vector3(quadSize * width / 2.0f, maxHeight / 4.0f, quadSize * depth / 2.0f);
    }

    private void CreatePlaneMesh()
    {
        planeMesh = GetComponent<MeshFilter>().mesh;
        //  create plane mesh for reflection
        Vector3[] planeVertices = new Vector3[4];
        Vector3[] planeNormals = new Vector3[4];
        int[] planeTriangles = new int[6];
        planeVertices[0] = new Vector3();
        planeVertices[1] = new Vector3(quadSize * (depth - 1), 0, quadSize * (width - 1));
        planeVertices[2] = new Vector3(quadSize * (depth - 1), 0, 0);
        planeVertices[3] = new Vector3(0, 0, quadSize * (width - 1));
        planeNormals[0] = Vector3.up;
        planeNormals[1] = Vector3.up;
        planeNormals[2] = Vector3.up;
        planeNormals[3] = Vector3.up;
        planeTriangles[0] = 0;
        planeTriangles[1] = 2;
        planeTriangles[2] = 1;
        planeTriangles[3] = 0;
        planeTriangles[4] = 1;
        planeTriangles[5] = 3;
        planeMesh.vertices = planeVertices;
        planeMesh.triangles = planeTriangles;
        planeMesh.normals = planeNormals;
    }

    private void UpdateCameraModes(Camera src, Camera dest)
    {
        if (dest == null)
        {
            return;
        }
        // set water camera to clear the same way as current camera
        dest.clearFlags = src.clearFlags;
        dest.backgroundColor = src.backgroundColor;
        if (src.clearFlags == CameraClearFlags.Skybox)
        {
            Skybox sky = src.GetComponent<Skybox>();
            Skybox mysky = dest.GetComponent<Skybox>();
            if (!sky || !sky.material)
            {
                mysky.enabled = false;
            }
            else
            {
                mysky.enabled = true;
                mysky.material = sky.material;
            }
        }
        // update other values to match current camera.
        // even if we are supplying custom camera&projection matrices,
        // some of values are used elsewhere (e.g. skybox uses far plane)
        dest.farClipPlane = src.farClipPlane;
        dest.nearClipPlane = src.nearClipPlane;
        dest.orthographic = src.orthographic;
        dest.fieldOfView = src.fieldOfView;
        dest.aspect = src.aspect;
        dest.orthographicSize = src.orthographicSize;
    }

    // On-demand create any objects we need for water
    private void CreateWaterObjects(Camera currentCamera, out Camera reflectionCamera)
    {
        reflectionCamera = null;

        // Reflection render texture
        if (!reflectionTex || m_OldReflectionTextureSize != textureSize)
        {
            if (reflectionTex)
            {
                DestroyImmediate(reflectionTex);
            }
            reflectionTex = new RenderTexture(textureSize, textureSize, 16);
            reflectionTex.name = "__WaterReflection" + GetInstanceID();
            reflectionTex.isPowerOfTwo = true;
            reflectionTex.hideFlags = HideFlags.DontSave;
            m_OldReflectionTextureSize = textureSize;
        }

        // Camera for reflection
        m_ReflectionCameras.TryGetValue(currentCamera, out reflectionCamera);
        if (!reflectionCamera) // catch both not-in-dictionary and in-dictionary-but-deleted-GO
        {
            GameObject go = new GameObject("Water Refl Camera id" + GetInstanceID() + " for " + currentCamera.GetInstanceID(), typeof(Camera), typeof(Skybox));
            reflectionCamera = go.GetComponent<Camera>();
            reflectionCamera.enabled = false;
            reflectionCamera.transform.position = transform.position;
            reflectionCamera.transform.rotation = transform.rotation;
            reflectionCamera.gameObject.AddComponent<FlareLayer>();
            go.hideFlags = HideFlags.HideAndDontSave;
            m_ReflectionCameras[currentCamera] = reflectionCamera;
        }
    }

    static void CalculateReflectionMatrix(ref Matrix4x4 reflectionMat, Vector4 plane)
    {
        reflectionMat.m00 = (1F - 2F * plane[0] * plane[0]);
        reflectionMat.m01 = (-2F * plane[0] * plane[1]);
        reflectionMat.m02 = (-2F * plane[0] * plane[2]);
        reflectionMat.m03 = (-2F * plane[3] * plane[0]);

        reflectionMat.m10 = (-2F * plane[1] * plane[0]);
        reflectionMat.m11 = (1F - 2F * plane[1] * plane[1]);
        reflectionMat.m12 = (-2F * plane[1] * plane[2]);
        reflectionMat.m13 = (-2F * plane[3] * plane[1]);

        reflectionMat.m20 = (-2F * plane[2] * plane[0]);
        reflectionMat.m21 = (-2F * plane[2] * plane[1]);
        reflectionMat.m22 = (1F - 2F * plane[2] * plane[2]);
        reflectionMat.m23 = (-2F * plane[3] * plane[2]);

        reflectionMat.m30 = 0F;
        reflectionMat.m31 = 0F;
        reflectionMat.m32 = 0F;
        reflectionMat.m33 = 1F;
    }

    // Given position/normal of the plane, calculates plane in camera space.
    private Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal, float sideSign)
    {
        Vector3 offsetPos = pos + normal * clipPlaneOffset;
        Matrix4x4 m = cam.worldToCameraMatrix;
        Vector3 cpos = m.MultiplyPoint(offsetPos);
        Vector3 cnormal = m.MultiplyVector(normal).normalized * sideSign;
        return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
    }

    /// <summary>
    /// Calculates the Y-value of the water-heightfield at the given X- and Z-values of a position in world space.
    /// </summary>
    /// <param name="worldPosition">X- and Z- Value will be taken from this Vector3</param>
    public float getHeightAtWorldPosition(Vector3 worldPosition)
    {
        int k, m;
        k = Mathf.Max(Mathf.Min(Mathf.RoundToInt((worldPosition.x - transform.position.x) / quadSize), width - 1), 0);
        m = Mathf.Max(Mathf.Min(Mathf.RoundToInt((worldPosition.z - transform.position.z) / quadSize), depth - 1), 0);

        float x1, x2, x3, x4;
        if (useLinearEquation)
        {
            //	get surrounding height values at the vertex position (can be randomly displaced)
            x1 = hf[k * depth + m].height;
            x2 = hf[Mathf.Min((k + 1), width - 1) * depth + Mathf.Min(m + 1, depth - 1)].height;
            x3 = hf[k * depth + Mathf.Min(m + 1, depth - 1)].height;
            x4 = hf[Mathf.Min((k + 1), width - 1) * depth + m].height;
        }
        else
        {
            //	get surrounding height values at the vertex position (can be randomly displaced)
            x1 = U[k * depth + m].x;
            x2 = U[Mathf.Min((k + 1), width - 1) * depth + Mathf.Min(m + 1, depth - 1)].x;
            x3 = U[k * depth + Mathf.Min(m + 1, depth - 1)].x;
            x4 = U[Mathf.Min((k + 1), width - 1) * depth + m].x;
        }
        //	get x and y value between 0 and 1 for interpolation
        float x = ((worldPosition.x - transform.position.x) / quadSize - k);
        float y = ((worldPosition.z - transform.position.z) / quadSize - m);

        //	bilinear interpolation to get height at vertex i
        //	note if x == 0 and y == 0 vertex position is at heightfield position.
        float resultingHeight = (x1 * (1 - x) + x4 * (x)) * (1 - y) + (x3 * (1 - x) + x2 * (x)) * (y);

        return resultingHeight;
    }
}
