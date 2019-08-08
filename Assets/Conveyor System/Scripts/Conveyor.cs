using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Jobs;

namespace NoHalfMeasures
{
    public class Conveyor : MonoBehaviour, IConveyor
    {
        [Serializable]
        public struct ConveyorData
        {
            [Header("Mesh")]
            public Mesh baseMesh;

            [Header("Material")]
            public Material[] baseMats;

            [Header("Stats")]
            public int itemsPerMin;

            public int maxLength;
        }

        public bool bRenderItems = true;
        public bool bDebug = true;

        private float heightOffset = 1.0f;
        private float animateDistance = 75.0f;

        public Mesh meshOne;
        public Mesh meshTwo;

        public Material mat;

        //
        public ConveyorData conveyorData;

        private float itemsPerSecond;

        public int length;

        //

        private List<Vector3> nodes = new List<Vector3>();

        // Each Slot on the conveyors legth in which an item can physicaly exist.
        private List<int> slots;

        //private List<Matrix4x4> staticConPoints = new List<Matrix4x4>();
        private List<Matrix4x4> staticItemPoints = new List<Matrix4x4>();

        // For Jobs
        private NativeArray<Matrix4x4> result;
        private NativeArray<Vector3> points;

        // Build Snapping
        [SerializeField]
        private Vector3 startSnap;
        [SerializeField]
        private Vector3 endSnap;
        public Vector3 EndSnap { get => endSnap; }

        private MeshFilter meshF;

        private bool inited = false;

        struct MoveJob : IJobParallelFor
        {
            [ReadOnly] public Vector3 start;
            [ReadOnly] public NativeArray<Vector3> points;
            [ReadOnly] public float time;

            public NativeArray<Matrix4x4> result;

            public void Execute(int i)
            {
                Vector3 p = start;

                if (i > 0)
                    p += points[i - 1];

                Vector3 pos = Vector3.Lerp(p, new Vector3(start.x + points[i].x, start.y + points[i].y, start.z + points[i].z), time);
                result[i] = Matrix4x4.TRS(new Vector3(pos.x, pos.y, pos.z), /*Quaternion.FromToRotation(Vector3.forward, -points[0])*/Quaternion.LookRotation(points[i]), Vector3.one);
            }
        }

        private void Start()
        {
            Init();
        }

        private Vector3 fwd = new Vector3();
        public virtual void Init()
        {
            OnDisable();
            OnEnable();

            itemsPerSecond = 60.0f / conveyorData.itemsPerMin;
            fwd = this.transform.forward;

            slots = new List<int>(new int[length]);
            for(int i = 0; i < slots.Count; i++)
            {
                slots[i] = i % 2 == 0 ? 1 : 2;
            }
            
            for (int i = 0; i < length; i++)
            {
                nodes.Add(new Vector3(fwd.x * (i + 1), fwd.y * (i + 1) /*+ heightOffset*/, fwd.z * (i + 1)));
            }

            points.CopyFrom(nodes.ToArray());

            for (int i = 0; i < nodes.Count; i++)
            {
                staticItemPoints.Add(Matrix4x4.TRS(new Vector3(this.transform.position.x + nodes[i].x, this.transform.position.y + nodes[i].y + 1f, this.transform.position.z + nodes[i].z), Quaternion.FromToRotation(Vector3.forward, -fwd), Vector3.one));
            }

            startSnap = new Vector3(this.transform.position.x, this.transform.position.y, this.transform.position.z);
            endSnap = this.transform.position + nodes[nodes.Count - 1];


            // Building the mesh
            meshF = this.GetComponent<MeshFilter>();
            BuildMesh();

            this.gameObject.AddComponent<BoxCollider>();

            inited = true;
        }

        private void BuildMesh()
        {
            List<CombineInstance> combineInstances = new List<CombineInstance>();
            List<Mesh> meshes = new List<Mesh>();

            for (int s = 0; s < conveyorData.baseMesh.subMeshCount; s++)
            {
                combineInstances = new List<CombineInstance>();

                for (int i = 0; i < nodes.Count; i++)
                {
                    CombineInstance ci = new CombineInstance();
                    ci.mesh = conveyorData.baseMesh;
                    ci.subMeshIndex = s;
                    ci.mesh.SetTriangles(conveyorData.baseMesh.GetTriangles(s), s);
                    ci.transform = Matrix4x4.TRS(new Vector3(0, 0, i), Quaternion.identity, Vector3.one);

                    combineInstances.Add(ci);
                }

                Mesh m = new Mesh();
                m.CombineMeshes(combineInstances.ToArray(), true, true);
                m.RecalculateBounds();
                m.RecalculateNormals();
                m.Optimize();

                meshes.Add(m);
            }

            combineInstances = new List<CombineInstance>();

            for (int i = 0; i < meshes.Count; i++)
            {
                CombineInstance ci = new CombineInstance();
                ci.mesh = meshes[i];
                ci.transform = Matrix4x4.TRS(new Vector3(0, 0, 0)/*nodes[i]*/, /*Quaternion.FromToRotation(Vector3.forward, -fwd)*/Quaternion.identity, new Vector3(1f, 1f, 1f));

                combineInstances.Add(ci);
            }

            Mesh newMesh = new Mesh();
            newMesh.Clear();
            newMesh.CombineMeshes(combineInstances.ToArray(), false, true);
            newMesh.RecalculateBounds();
            newMesh.RecalculateNormals();
            newMesh.Optimize();

            meshF.mesh = newMesh;
        }

        void OnEnable()
        {
            result = new NativeArray<Matrix4x4>(length, Allocator.Persistent);
            points = new NativeArray<Vector3>(length, Allocator.Persistent);
        }

        void OnDisable()
        {
            if(result.IsCreated)
                result.Dispose();
            if (points.IsCreated)
                points.Dispose();
        }

        private float time;
        JobHandle handleCalculate;
        private void Update()
        {
            if (!inited)
                return;

            time += Time.deltaTime / itemsPerSecond;

            if (time > itemsPerSecond)
            {
                time = time - itemsPerSecond;

                for (int i = slots.Count - 1; i > -1; i--)
                {
                    if (slots[i] != 0)
                    {
                        AssignSlot(slots[i], i + 1);
                        slots[i] = 0;
                    }

                    if (i == 0 && QuedItem != 0)
                    {
                        slots[i] = QuedItem;
                        QuedItem = 0;
                    }

                    //slots[i] = i - 1 > -1 ? slots[i - 1] : 0;

                    //if (i == slots.Count - 1 && slots[i] == 1)
                    //    slots[0] = 1;
                }
            }


            if (bRenderItems)
            {
                if (Vector3.Distance(Camera.main.transform.position, this.transform.position) <= animateDistance)
                {
                    MoveJob job = new MoveJob()
                    {
                        time = time,
                        start = new Vector3(this.transform.position.x, this.transform.position.y + heightOffset, this.transform.position.z),
                        points = points,
                        result = result
                    };

                    handleCalculate = job.Schedule(result.Length, 1);
                }
            }
        }

        void LateUpdate()
        {
            if (!inited)
                return;

            if (bRenderItems)
                handleCalculate.Complete();

            if (bRenderItems)
            {
                if (Vector3.Distance(Camera.main.transform.position, this.transform.position) <= animateDistance)
                {
                    //if (bInstancedItems)
                    //{
                    //    Matrix4x4[] matrices = new Matrix4x4[length];
                    //    result.CopyTo(matrices);
                    //    Graphics.DrawMeshInstanced(itemMesh, 0, matCrate, matrices, length, null, UnityEngine.Rendering.ShadowCastingMode.On, true, 0, Camera.current);
                    //}
                    //else
                    {
                        for (int i = 0; i < result.Length; i++)
                        {
                            if (slots[i] != 0)
                                switch (slots[i])
                                {
                                    case 1: // Cube
                                        Graphics.DrawMesh(meshOne, result[i], mat, 0, Camera.current);
                                        break;
                                    case 2: // Sphere
                                        Graphics.DrawMesh(meshTwo, result[i], mat, 0, Camera.current);
                                        break;
                                    default:
                                        break;
                                }
                        }
                    }
                }
                else
                {
                    //if (bInstancedItems)
                    //{
                    //    Graphics.DrawMeshInstanced(slots[i].itemMesh, 0, slots[i].itemMat, staticItemPoints, null, UnityEngine.Rendering.ShadowCastingMode.On, true, 0, Camera.current);
                    //}
                    //else
                    {
                        for (int i = 0; i < staticItemPoints.Count; i++)
                        {
                            if (slots[i] != 0)
                                switch (slots[i])
                                {
                                    case 1: // Cube
                                        Graphics.DrawMesh(meshOne, staticItemPoints[i], mat, 0, Camera.current);
                                        break;
                                    case 2: // Sphere
                                        Graphics.DrawMesh(meshTwo, staticItemPoints[i], mat, 0, Camera.current);
                                        break;
                                    default:
                                        break;
                                }
                        }
                    }
                }
            }
        }

        private int QuedItem;
        private void AssignSlot(int item, int index)
        {
            if (index >= slots.Count)
            {
                QuedItem = item;
                return;
            }

            if (slots[index] != 0)
            {
                Debug.LogWarning("Conveyor::AssignSlot - Function is being called on a slot index that is being used.");
            }
            else
            {
                slots[index] = item;
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (bDebug & Application.isPlaying)
            {
                for (int n = 0; n < nodes.Count; n++)
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawSphere(this.transform.position + nodes[n], .1f);
                }

                Gizmos.color = Color.cyan;
                Gizmos.DrawSphere(startSnap, .1f);

                Gizmos.color = Color.magenta;
                Gizmos.DrawSphere(EndSnap, .1f);
            }
        }
    }
}