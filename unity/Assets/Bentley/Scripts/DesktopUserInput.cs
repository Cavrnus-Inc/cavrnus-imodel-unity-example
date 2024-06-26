/*---------------------------------------------------------------------------------------------
* Copyright (c) Bentley Systems, Incorporated. All rights reserved.
* See LICENSE.md in the project root for license terms and full copyright notice.
*--------------------------------------------------------------------------------------------*/

using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Bentley.Protobuf;
using CavrnusSdk.API;
using UnityEngine;

namespace Bentley
{
    public interface IUserInput
    {
        void OnUpdate();
        void OnGUI();
    }

    public class DesktopUserInput : IUserInput
    {
        private readonly BackendRouter _backend;
        private readonly Material _selectionMaterial;
        private readonly SavedViews _savedViews;

        private MeshRenderer _selectedRenderer;
        private Material _selectedOriginalMaterial;

        private readonly Camera _camera;
        private readonly Transform _cameraTransform;

        private GUIStyle _propertiesBoxGuiStyle;
        private GUIContent _propertiesText;
        private const float PropertiesBoxWidth = 400;
        private float _propertiesBoxHeight;

        private CavrnusSpaceConnection cavrnusSpaceConnection;

        public DesktopUserInput(CavrnusSpaceConnection cavrnusSpaceConnection, Material opaqueMaterial, BackendRouter backend, SavedViews savedViews)
        {
            this.cavrnusSpaceConnection = cavrnusSpaceConnection;
            
            WaitAndBind();
            
            _backend = backend;
            _selectionMaterial = new Material(opaqueMaterial) { color = Color.magenta };
            _savedViews = savedViews;

            _camera = Camera.main;
            _cameraTransform = _camera.transform;
        }
        
        async Task WaitAndBind()
        {
            // Wait for 5 seconds
            await Task.Delay(5000);
            
            cavrnusSpaceConnection.BindStringPropertyValue("Data", "CurrentlySelected", id => {
                ClearSelection();
                CavrnusSetSelection(id);
            });
        }

        public void OnUpdate()
        {
            if (Input.GetMouseButtonDown(0)) SetSelectionFromCursor();
            if (Input.GetKeyDown(KeyCode.Escape)) ClearSelection();

            if (Input.GetKeyDown(KeyCode.LeftArrow)) _savedViews.ApplyPreviousView();
            if (Input.GetKeyDown(KeyCode.RightArrow)) _savedViews.ApplyNextView();

            UpdateCamera();
        }

        public void OnGUI()
        {
            if (_propertiesBoxGuiStyle == null)
            {
                _propertiesBoxGuiStyle = new GUIStyle(GUI.skin.box);
                _propertiesBoxGuiStyle.alignment = TextAnchor.UpperLeft;
            }

            if (_propertiesText != null)
            {
                float height = System.Math.Min(Screen.height - 20, _propertiesBoxHeight);
                GUI.Box(new Rect(10, 10, 300, System.Math.Min(Screen.height - 20, height)), _propertiesText, _propertiesBoxGuiStyle);
            }
        }

        private void ClearSelection()
        {
            if (_selectedRenderer == null) return;

            _selectedRenderer.sharedMaterial = _selectedOriginalMaterial;
            _selectedRenderer = null;
            _selectedOriginalMaterial = null;
            _propertiesText = null;
        }

        private void SetSelectionFromCursor()
        {
            ClearSelection();
            if (!Physics.Raycast(_camera.ScreenPointToRay(Input.mousePosition), out RaycastHit hitInfo))
                return;
            
            // _selectedRenderer = hitInfo.transform.GetComponent<MeshRenderer>();
            
            cavrnusSpaceConnection.PostStringPropertyUpdate("Data", "CurrentlySelected",hitInfo.transform.GetComponent<MeshRenderer>().name);

            // _selectedOriginalMaterial = _selectedRenderer.sharedMaterial;
            // _selectedRenderer.material = _selectionMaterial;
            //
            // var propRequestWrapper = new RequestWrapper
            // {
            //     ElementPropertiesRequest = new ElementPropertiesRequest { ElementId = _selectedRenderer.name }
            // };
            // _backend.SendRequest(propRequestWrapper, propReplyWrapper =>
            // {
            //     var propStringBuilder = new StringBuilder(512);
            //     BuildPropertiesString(propStringBuilder, propReplyWrapper.ElementPropertiesReply.Root, "");
            //
            //     _propertiesText = new GUIContent(propStringBuilder.ToString());
            //     _propertiesBoxHeight = _propertiesBoxGuiStyle.CalcHeight(_propertiesText, PropertiesBoxWidth);
            // });
        }

        private void CavrnusSetSelection(string remoteId)
        {
            var selectedOb = GameObject.Find(remoteId);
            if (selectedOb == null) {
                Debug.Log($"Can't find: {remoteId}!");
                return;
            }
            
            _selectedRenderer = selectedOb.GetComponent<MeshRenderer>();
            _selectedOriginalMaterial = _selectedRenderer.material;
            _selectedRenderer.material = _selectionMaterial;

            var propRequestWrapper = new RequestWrapper
            {
                ElementPropertiesRequest = new ElementPropertiesRequest { ElementId = _selectedRenderer.name }
            };
            _backend.SendRequest(propRequestWrapper, propReplyWrapper =>
            {
                var propStringBuilder = new StringBuilder(512);
                BuildPropertiesString(propStringBuilder, propReplyWrapper.ElementPropertiesReply.Root, "");

                _propertiesText = new GUIContent(propStringBuilder.ToString());
                _propertiesBoxHeight = _propertiesBoxGuiStyle.CalcHeight(_propertiesText, PropertiesBoxWidth);
            });
        }

        private static void BuildPropertiesString(StringBuilder sb, ElementPropertiesReplyEntry entry, string indent)
        {
            if (entry.Children.Count == 0)
            {
                sb.AppendLine($"{indent}{entry.Label}: {entry.Value}");
                return;
            }

            sb.AppendLine($"{indent}{entry.Label}");
            foreach (ElementPropertiesReplyEntry child in entry.Children)
            {
                string childIndent = indent + "  ";
                BuildPropertiesString(sb, child, childIndent);
            }
        }

        private void UpdateCamera()
        {
            var mousePos = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));

            // Middle mouse panning
            if (Input.GetMouseButton(2))
            {
                const float panSpeed = 50.0f;
                _cameraTransform.Translate(-mousePos.x * Time.deltaTime * panSpeed, -mousePos.y * Time.deltaTime * panSpeed, 0);
                return;
            }

            // Translation zoom with scroll wheel
            float mouseWheel = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(mouseWheel) > 0.01f)
            {
                const float zoomSpeed = 25.0f;
                _cameraTransform.Translate(0, 0, mouseWheel * zoomSpeed, Space.Self);
                return;
            }

            // Mouse look when right-click is down
            if (Input.GetMouseButton(1))
            {
                const float lookSpeed = 2.0f;

                Vector3 eulerAngles = _cameraTransform.eulerAngles;
                eulerAngles.y += lookSpeed * mousePos.x;
                eulerAngles.x -= lookSpeed * mousePos.y;
                _cameraTransform.eulerAngles = eulerAngles;
                // No return, can be combined with WASD movement
            }

            // WASD movement
            Vector3 translation = Vector3.zero;
            bool moved = false;

            const float flySpeed = 10.0f;
            const float shiftMultiplier = 2.5f;

            float frameFlySpeed = Time.deltaTime * flySpeed;
            if (Input.GetKey(KeyCode.LeftShift)) frameFlySpeed *= shiftMultiplier;
            if (Input.GetKey(KeyCode.S)) { translation.z -= frameFlySpeed; moved = true; }
            if (Input.GetKey(KeyCode.W)) { translation.z += frameFlySpeed; moved = true; }
            if (Input.GetKey(KeyCode.A)) { translation.x -= frameFlySpeed; moved = true; }
            if (Input.GetKey(KeyCode.D)) { translation.x += frameFlySpeed; moved = true; }
            if (Input.GetKey(KeyCode.Q)) { translation.y -= frameFlySpeed; moved = true; }
            if (Input.GetKey(KeyCode.E)) { translation.y += frameFlySpeed; moved = true; }

            if (moved) _cameraTransform.Translate(translation, Space.Self);
        }
    }
}
