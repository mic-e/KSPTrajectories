﻿/*
Trajectories
Copyright 2014, Youen Toupin

This file is part of Trajectories, under MIT license.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Trajectories
{
    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    public class MapOverlay : MonoBehaviour
    {
        private List<GameObject> meshes = new List<GameObject>();
        private bool displayEnabled = false;

        private Material lineMaterial;
        private float lineWidth = 0.002f;

        public void LateUpdate()
        {
            if ((HighLogic.LoadedScene != GameScenes.FLIGHT && HighLogic.LoadedScene != GameScenes.TRACKSTATION) || !MapView.MapIsEnabled || MapView.MapCamera == null)
            {
                setDisplayEnabled(false);
                return;
            }

            setDisplayEnabled(true);
            refreshMesh();
        }

        private void setDisplayEnabled(bool enabled)
        {
            enabled = enabled && Settings.fetch.DisplayTrajectories;

            if (displayEnabled == enabled)
                return;
            displayEnabled = enabled;

            foreach (var mesh in meshes)
            {
                mesh.GetComponent<MeshRenderer>().enabled = enabled;
            }
        }

        private GameObject GetMesh(CelestialBody body, Material material)
        {
            GameObject obj = null;
            foreach (var mesh in meshes)
            {
                if (!mesh.activeSelf)
                {
                    mesh.SetActive(true);
                    obj = mesh;
                    break;
                }
            }

            if (obj == null)
            {
                ScreenMessages.PostScreenMessage("adding trajectory mesh " + meshes.Count);

                var newMesh = new GameObject();
                newMesh.AddComponent<MeshFilter>();
                var renderer = newMesh.AddComponent<MeshRenderer>();
                renderer.enabled = displayEnabled;
                renderer.castShadows = false;
                renderer.receiveShadows = false;
                newMesh.layer = 10;

                meshes.Add(newMesh);

                obj = newMesh;
            }

            obj.transform.parent = ScaledSpace.Instance.scaledSpaceTransforms.First(t => t.name == body.name);
            obj.transform.localScale = Vector3.one * (0.0001667f / obj.transform.parent.localScale.x);
            obj.transform.localPosition = Vector3.zero;
            obj.transform.rotation = Quaternion.identity;

            obj.renderer.sharedMaterial = material;

            return obj;
        }

        private void refreshMesh()
        {
            foreach (var mesh in meshes)
            {
                mesh.SetActive(false);
            }

            // material from RemoteTech
            if(lineMaterial == null)
                lineMaterial = new Material("Shader \"Vertex Colors/Alpha\" {Category{Tags {\"Queue\"=\"Transparent\" \"IgnoreProjector\"=\"True\" \"RenderType\"=\"Transparent\"}SubShader {Cull Off ZWrite On Blend SrcAlpha OneMinusSrcAlpha Pass {BindChannels {Bind \"Color\", color Bind \"Vertex\", vertex}}}}}");

            foreach (var patch in Trajectory.fetch.patches)
            {
                if (patch.isAtmospheric && patch.atmosphericTrajectory.Length < 2)
                    continue;

                var obj = GetMesh(patch.startingState.referenceBody, lineMaterial);
                var mesh = obj.GetComponent<MeshFilter>().mesh;
                
                if (patch.isAtmospheric)
                {
                    initMeshFromTrajectory(obj.transform, mesh, patch.atmosphericTrajectory, Color.red);
                }
                else
                {
                    initMeshFromOrbit(obj.transform, mesh, patch.spaceOrbit, patch.startingState.time, patch.endTime - patch.startingState.time, Color.white);
                }

                if (patch.impactPosition.HasValue)
                {
                    obj = GetMesh(patch.startingState.referenceBody, lineMaterial);
                    mesh = obj.GetComponent<MeshFilter>().mesh;
                    initMeshFromImpact(obj.transform, mesh, patch.impactPosition.Value, Color.red);
                }
            }

            Vector3? targetPosition = Trajectory.fetch.targetPosition;
            if (targetPosition.HasValue)
            {
                var obj = GetMesh(Trajectory.fetch.targetBody, lineMaterial);
                var mesh = obj.GetComponent<MeshFilter>().mesh;
                initMeshFromImpact(obj.transform, mesh, targetPosition.Value, Color.green);
            }
        }

        private void initMeshFromOrbit(Transform meshTransform, Mesh mesh, Orbit orbit, double startTime, double duration, Color color)
        {
            int steps = 256;

            var vertices = new Vector3[steps * 2 + 2];
            var triangles = new int[steps * 6];

            Vector3 camPos = meshTransform.InverseTransformPoint(MapView.MapCamera.transform.position);

            Vector3 prevMeshPos = orbit.getRelativePositionAtUT(startTime - duration / (double)steps);
            for (int i = 0; i <= steps; ++i)
            {
                double time = startTime + duration * (double)i / (double)steps;

                Vector3 curMeshPos = orbit.getRelativePositionAtUT(time);
                if (Settings.fetch.BodyFixedMode) {
                    curMeshPos = Trajectory.calculateRotatedPosition(orbit.referenceBody, curMeshPos, time);
                }

                // compute an "up" vector that is orthogonal to the trajectory orientation and to the camera vector (used to correctly orient quads to always face the camera)
                Vector3 up = Vector3.Cross(curMeshPos - prevMeshPos, camPos - curMeshPos).normalized * (lineWidth * Vector3.Distance(camPos, curMeshPos));

                // add a segment to the trajectory mesh
                vertices[i * 2 + 0] = curMeshPos - up;
                vertices[i * 2 + 1] = curMeshPos + up;

                if (i > 0)
                {
                    int idx = (i - 1) * 6;
                    triangles[idx + 0] = (i - 1) * 2 + 0;
                    triangles[idx + 1] = (i - 1) * 2 + 1;
                    triangles[idx + 2] = i * 2 + 1;

                    triangles[idx + 3] = (i - 1) * 2 + 0;
                    triangles[idx + 4] = i * 2 + 1;
                    triangles[idx + 5] = i * 2 + 0;
                }

                prevMeshPos = curMeshPos;
            }

            var colors = new Color[vertices.Length];
            for (int i = 0; i < colors.Length; ++i)
                colors[i] = color;

            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
        }

        private void initMeshFromTrajectory(Transform meshTransform, Mesh mesh, Vector3[] trajectory, Color color)
        {
            var vertices = new Vector3[trajectory.Length * 2];
            var triangles = new int[(trajectory.Length-1) * 6];

            Vector3 camPos = meshTransform.InverseTransformPoint(MapView.MapCamera.transform.position);

            Vector3 prevMeshPos = trajectory[0] - (trajectory[1]-trajectory[0]);
            for(int i = 0; i < trajectory.Length; ++i)
            {
                Vector3 curMeshPos = trajectory[i];
                // the fixed-body rotation transformation has already been applied in AddPatch.

                // compute an "up" vector that is orthogonal to the trajectory orientation and to the camera vector (used to correctly orient quads to always face the camera)
                Vector3 up = Vector3.Cross(curMeshPos - prevMeshPos, camPos - curMeshPos).normalized * (lineWidth * Vector3.Distance(camPos, curMeshPos));

                // add a segment to the trajectory mesh
                vertices[i * 2 + 0] = curMeshPos - up;
                vertices[i * 2 + 1] = curMeshPos + up;

                if (i > 0)
                {
                    int idx = (i - 1) * 6;
                    triangles[idx + 0] = (i - 1) * 2 + 0;
                    triangles[idx + 1] = (i - 1) * 2 + 1;
                    triangles[idx + 2] = i * 2 + 1;

                    triangles[idx + 3] = (i - 1) * 2 + 0;
                    triangles[idx + 4] = i * 2 + 1;
                    triangles[idx + 5] = i * 2 + 0;
                }

                prevMeshPos = curMeshPos;
            }

            var colors = new Color[vertices.Length];
            for (int i = 0; i < colors.Length; ++i)
                colors[i] = color;

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.colors = colors;
            mesh.RecalculateBounds();
        }

        private void initMeshFromImpact(Transform meshTransform, Mesh mesh, Vector3 impactPosition, Color color)
        {
            var vertices = new Vector3[8];
            var triangles = new int[12];

            Vector3 camPos = meshTransform.InverseTransformPoint(MapView.MapCamera.transform.position);

            Vector3 crossV1 = Vector3.Cross(impactPosition, Vector3.right).normalized;
            Vector3 crossV2 = Vector3.Cross(impactPosition, crossV1).normalized;
            
            float crossThickness = lineWidth * Vector3.Distance(camPos, impactPosition);
            float crossSize = crossThickness * 10.0f;

            vertices[0] = impactPosition - crossV1 * crossSize + crossV2 * crossThickness;
            vertices[1] = impactPosition - crossV1 * crossSize - crossV2 * crossThickness;
            vertices[2] = impactPosition + crossV1 * crossSize + crossV2 * crossThickness;
            vertices[3] = impactPosition + crossV1 * crossSize - crossV2 * crossThickness;

            triangles[0] = 0;
            triangles[1] = 1;
            triangles[2] = 3;
            triangles[3] = 0;
            triangles[4] = 3;
            triangles[5] = 2;

            vertices[4] = impactPosition - crossV2 * crossSize - crossV1 * crossThickness;
            vertices[5] = impactPosition - crossV2 * crossSize + crossV1 * crossThickness;
            vertices[6] = impactPosition + crossV2 * crossSize - crossV1 * crossThickness;
            vertices[7] = impactPosition + crossV2 * crossSize + crossV1 * crossThickness;

            triangles[6] = 4;
            triangles[7] = 5;
            triangles[8] = 7;
            triangles[9] = 4;
            triangles[10] = 7;
            triangles[11] = 6;

            var colors = new Color[vertices.Length];
            for (int i = 0; i < colors.Length; ++i)
                colors[i] = color;

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.colors = colors;
            mesh.RecalculateBounds();
        }

        public void OnDestroy()
        {
            Settings.fetch.Save();
        }
    }
}
