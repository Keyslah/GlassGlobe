using System;
using System.Collections.Generic;
using UnityEngine;

namespace GlassGlobe
{
    [ExecuteAlways]
    public sealed class CountryLabelController : MonoBehaviour
    {
        public CountryBorderRenderer borderRenderer;
        public GlobeRenderer globe;
        public Camera targetCamera;
        public bool showCountryLabels = true;
        public bool showContinentLabels = true;
        public float surfaceOffset = 0.18f;
        public float characterSize = 0.085f;
        public Color labelColor = new Color(0.92f, 0.98f, 1f, 0.95f);

        private readonly List<TextMesh> labels = new List<TextMesh>();

        private void Reset()
        {
            borderRenderer = FindFirstObjectByType<CountryBorderRenderer>();
            globe = FindFirstObjectByType<GlobeRenderer>();
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                targetCamera = mainCamera;
            }
        }

        private void OnEnable()
        {
            RebuildLabels();
        }

        private void LateUpdate()
        {
            FaceCamera();
        }

        public void RebuildLabels()
        {
            ResolveReferences();
            ClearGeneratedLabels();
            labels.Clear();

            if (borderRenderer == null || globe == null || borderRenderer.Outlines == null)
            {
                return;
            }

            foreach (CountryBorderRenderer.GeoOutline outline in borderRenderer.Outlines)
            {
                if (outline == null)
                {
                    continue;
                }

                if (outline.isCountry && !showCountryLabels)
                {
                    continue;
                }

                if (!outline.isCountry && !showContinentLabels)
                {
                    continue;
                }

                GameObject labelObject = new GameObject("Label - " + outline.name);
                labelObject.transform.SetParent(transform, false);
                labelObject.transform.position = globe.GeoToWorld(outline.labelCoordinate, surfaceOffset);

                TextMesh textMesh = labelObject.AddComponent<TextMesh>();
                textMesh.text = outline.name;
                textMesh.anchor = TextAnchor.MiddleCenter;
                textMesh.alignment = TextAlignment.Center;
                textMesh.characterSize = characterSize;
                textMesh.fontSize = 64;
                textMesh.color = labelColor;
                labels.Add(textMesh);
            }

            FaceCamera();
        }

        private void FaceCamera()
        {
            ResolveReferences();
            if (targetCamera == null)
            {
                return;
            }

            for (int index = labels.Count - 1; index >= 0; index--)
            {
                TextMesh label = labels[index];
                if (label == null)
                {
                    labels.RemoveAt(index);
                    continue;
                }

                Transform labelTransform = label.transform;
                Vector3 toCamera = targetCamera.transform.position - labelTransform.position;
                if (toCamera.sqrMagnitude > 0.0001f)
                {
                    labelTransform.rotation = Quaternion.LookRotation(-toCamera.normalized, targetCamera.transform.up);
                }
            }
        }

        private void ResolveReferences()
        {
            if (borderRenderer == null)
            {
                borderRenderer = FindFirstObjectByType<CountryBorderRenderer>();
            }

            if (globe == null)
            {
                globe = FindFirstObjectByType<GlobeRenderer>();
            }

            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }
        }

        private void ClearGeneratedLabels()
        {
            for (int index = transform.childCount - 1; index >= 0; index--)
            {
                Transform child = transform.GetChild(index);
                if (!child.name.StartsWith("Label - ", StringComparison.Ordinal))
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(child.gameObject);
                }
                else
                {
                    DestroyImmediate(child.gameObject);
                }
            }
        }
    }
}
