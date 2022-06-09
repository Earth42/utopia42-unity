using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using src.Canvas;
using src.Model;
using src.Utils;
using UnityEngine;
using UnityEngine.Networking;

namespace src.MetaBlocks.TdObjectBlock
{
    public class TdObjectBlockObject : MetaBlockObject
    {
        public const ulong DownloadLimitMb = 10;

        private GameObject tdObjectContainer;
        private GameObject tdObject;

        public Collider TdObjectCollider { private set; get; }

        private TdObjectFocusable tdObjectFocusable;

        private string currentUrl = "";

        private ObjectScaleRotationController scaleRotationController;
        private Transform selectHighlight;

        public override void OnDataUpdate()
        {
            LoadTdObject();
        }

        protected override void DoInitialize()
        {
            LoadTdObject();
        }

        public override void ShowFocusHighlight()
        {
            if (TdObjectCollider == null) return;
            if (TdObjectCollider is BoxCollider boxCollider)
                AdjustHighlightBox(Player.INSTANCE.tdObjectHighlightBox, boxCollider, true);
            else
            {
                Player.INSTANCE.RemoveHighlightMesh();
                Player.INSTANCE.tdObjectHighlightMesh = CreateMeshHighlight(World.INSTANCE.HighlightBlock);
            }
        }

        public override void RemoveFocusHighlight()
        {
            if (Player.INSTANCE.RemoveHighlightMesh()) return;
            Player.INSTANCE.tdObjectHighlightBox.gameObject.SetActive(false);
        }

        public override GameObject CreateSelectHighlight(Transform parent, bool show = true)
        {
            if (TdObjectCollider == null) return null;

            Transform highlight;

            if (TdObjectCollider is not BoxCollider boxCollider)
                highlight = CreateMeshHighlight(World.INSTANCE.SelectedBlock, show);
            else
            {
                highlight = Instantiate(Player.INSTANCE.tdObjectHighlightBox, default, Quaternion.identity);
                highlight.GetComponentInChildren<MeshRenderer>().material = World.INSTANCE.SelectedBlock;
                AdjustHighlightBox(highlight, boxCollider, show);
            }

            highlight.SetParent(parent, true);
            highlight.gameObject.name = "3d_object_highlight";

            return highlight.gameObject;
        }

        private Transform CreateMeshHighlight(Material material, bool active = true)
        {
            var go = TdObjectCollider.gameObject;
            var clone = Instantiate(go.transform, go.transform.parent);
            DestroyImmediate(clone.GetComponent<MeshCollider>());
            var renderer = clone.GetComponent<MeshRenderer>();
            renderer.enabled = active;
            renderer.material = material;
            return clone;
        }

        public override void ExitMovingState()
        {
            var props = new TdObjectBlockProperties(Block.GetProps() as TdObjectBlockProperties);
            if (tdObjectContainer == null || State != State.Ok) return;
            props.rotation = new SerializableVector3(tdObjectContainer.transform.eulerAngles);
            props.scale = new SerializableVector3(tdObjectContainer.transform.localScale);
            Block.SetProps(props, land);

            if (snackItem != null) SetupDefaultSnack();
            if (scaleRotationController == null) return;
            scaleRotationController.Detach();
            DestroyImmediate(scaleRotationController);
            scaleRotationController = null;
        }


        protected override void SetupDefaultSnack()
        {
            if (snackItem != null) snackItem.Remove();

            snackItem = Snack.INSTANCE.ShowLines(GetSnackLines(), () =>
            {
                if (!canEdit) return;
                if (Input.GetKeyDown(KeyCode.Z))
                {
                    RemoveFocusHighlight();
                    EditProps();
                }

                if (Input.GetKeyDown(KeyCode.V) && State == State.Ok)
                {
                    RemoveFocusHighlight();
                    GameManager.INSTANCE.ToggleMovingObjectState(this);
                }

                if (Input.GetButtonDown("Delete"))
                {
                    World.INSTANCE.TryDeleteMeta(new MetaPosition(transform.position));
                }
            });
        }

        public override void SetToMovingState()
        {
            if (snackItem != null)
            {
                snackItem.Remove();
                snackItem = null;
            }

            if (scaleRotationController == null)
            {
                scaleRotationController = gameObject.AddComponent<ObjectScaleRotationController>();
                scaleRotationController.Attach(tdObjectContainer.transform, tdObjectContainer.transform);
            }

            snackItem = Snack.INSTANCE.ShowLines(scaleRotationController.EditModeSnackLines, () =>
            {
                if (Input.GetKeyDown(KeyCode.X))
                {
                    GameManager.INSTANCE.ToggleMovingObjectState(this);
                }
            });
        }

        protected override void OnStateChanged(State state)
        {
            ((SnackItem.Text) snackItem)?.UpdateLines(GetSnackLines());
            if (state == State.Ok) return;

            // setting place holder
            DestroyObject();
            ResetContainer();
            tdObject = Block.type.CreatePlaceHolder(MetaBlockState.IsErrorState(state), true);
            tdObject.transform.SetParent(tdObjectContainer.transform, false);
            tdObject.SetActive(true);
            TdObjectCollider = tdObject.GetComponentInChildren<Collider>();
            tdObject.transform.SetParent(tdObjectContainer.transform, false);

            if (chunk == null) return;
            tdObjectFocusable = TdObjectCollider.gameObject.AddComponent<TdObjectFocusable>();
            tdObjectFocusable.Initialize(this);
        }

        protected virtual List<string> GetSnackLines()
        {
            var lines = new List<string>();
            if (canEdit)
            {
                lines.Add("Press Z for details");
                if (State == State.Ok)
                    lines.Add("Press V to move object");
                lines.Add("Press DEL to delete object");
            }

            var line = MetaBlockState.ToString(State, "3D object");
            if (line.Length > 0)
                lines.Add((lines.Count > 0 ? "\n" : "") + line);
            return lines;
        }

        private void LoadTdObject()
        {
            var properties = (TdObjectBlockProperties) Block.GetProps();
            if (properties == null)
            {
                UpdateState(State.Empty);
                return;
            }

            var scale = properties.scale?.ToVector3() ?? Vector3.one;
            var rotation = properties.rotation?.ToVector3() ?? Vector3.zero;
            var initialPosition = properties.initialPosition?.ToVector3() ?? Vector3.zero;

            if (currentUrl.Equals(properties.url) && State == State.Ok)
            {
                LoadGameObject(scale, rotation, initialPosition, properties.initialScale,
                    properties.detectCollision, properties.type);
            }
            else
            {
                UpdateState(State.Loading);
                var reinitialize = !currentUrl.Equals("") || properties.initialScale == 0;
                currentUrl = properties.url;
                StartCoroutine(LoadBytes(properties.url, properties.type, go =>
                {
                    DestroyObject();
                    TdObjectCollider = null;
                    ResetContainer();
                    tdObject = go;

                    LoadGameObject(scale, rotation, initialPosition, properties.initialScale,
                        properties.detectCollision, properties.type, reinitialize);
                }));
            }
        }

        private void SetPlaceHolder(bool error)
        {
        }

        private void ResetContainer()
        {
            tdObjectContainer = new GameObject("3d object container");
            tdObjectContainer.transform.SetParent(transform, false);
            tdObjectContainer.transform.localPosition = Vector3.zero;
            tdObjectContainer.transform.localScale = Vector3.one;
            tdObjectContainer.transform.eulerAngles = Vector3.zero;
        }

        private void SetMeshCollider(Transform colliderTransform)
        {
            Destroy(tdObjectFocusable);
            Destroy(TdObjectCollider);

            TdObjectCollider = TdObjectTools.PrepareMeshCollider(colliderTransform);
            if (chunk != null)
            {
                tdObjectFocusable = TdObjectCollider.gameObject.AddComponent<TdObjectFocusable>();
                tdObjectFocusable.Initialize(this);
            }
        }

        private void LoadGameObject(Vector3 scale, Vector3 rotation, Vector3 initialPosition,
            float initialScale, bool detectCollision, TdObjectBlockProperties.TdObjectType type,
            bool reinitialize = false)
        {
            if (TdObjectCollider == null)
            {
                TdObjectCollider = tdObject.AddComponent<BoxCollider>();
                if (chunk != null)
                {
                    tdObjectFocusable = tdObject.AddComponent<TdObjectFocusable>();
                    tdObjectFocusable.Initialize(this);
                }

                ((BoxCollider) TdObjectCollider).center = TdObjectTools.GetRendererCenter(tdObject);
                ((BoxCollider) TdObjectCollider).size =
                    TdObjectTools.GetRendererSize(((BoxCollider) TdObjectCollider).center, tdObject);
                tdObject.transform.SetParent(tdObjectContainer.transform, false);
            }

            if (reinitialize)
            {
                tdObject.transform.localScale = Vector3.one;
                tdObject.transform.localPosition = Vector3.zero;

                var size = ((BoxCollider) TdObjectCollider).size;
                var maxD = new[] {size.x, size.y, size.z}.Max();
                var newInitScale = maxD > 10f ? 10f / maxD : 1;
                tdObject.transform.localScale = newInitScale * Vector3.one;
                var newInitPosition = tdObjectContainer.transform.TransformPoint(Vector3.zero) -
                                      TdObjectCollider.transform.TransformPoint(((BoxCollider) TdObjectCollider)
                                          .center);

                if (Math.Abs(newInitScale - initialScale) > 0.0001 || newInitPosition != initialPosition)
                {
                    InitializeProps(newInitPosition, newInitScale);
                    return;
                }
            }

            tdObject.transform.localScale = initialScale * Vector3.one;
            tdObject.transform.localPosition = initialPosition;

            tdObjectContainer.transform.localScale = scale;
            tdObjectContainer.transform.eulerAngles = rotation;

            var colliderTransform = TdObjectTools.GetMeshColliderTransform(tdObject);
            if (colliderTransform != null && type == TdObjectBlockProperties.TdObjectType.GLB)
            {
                // replace box collider with mesh collider if any colliders are defined in the glb object
                if (TdObjectCollider is BoxCollider)
                    SetMeshCollider(colliderTransform);

                if (Block.land != null && !InLand(TdObjectCollider.GetComponent<MeshRenderer>()))
                {
                    UpdateState(State.OutOfBound);
                    return;
                }
            }
            else if (Block.land != null && !InLand((BoxCollider) TdObjectCollider))
            {
                UpdateState(State.OutOfBound);
                return;
            }

            TdObjectCollider.gameObject.layer =
                detectCollision ? LayerMask.NameToLayer("Default") : LayerMask.NameToLayer("3DColliderOff");

            UpdateState(State.Ok);
            // chunk.UpdateMetaHighlight(new VoxelPosition(Vectors.FloorToInt(transform.position))); // TODO: fix on focus
        }

        private void DestroyObject(bool immediate = true)
        {
            if (tdObjectFocusable != null)
            {
                tdObjectFocusable.UnFocus();
                tdObjectFocusable = null;
            }

            if (tdObject != null)
            {
                foreach (var renderer in tdObject.GetComponentsInChildren<Renderer>())
                foreach (var mat in renderer.sharedMaterials)
                {
                    if (mat == null) continue;
                    if (immediate)
                    {
                        DestroyImmediate(mat.mainTexture);
                        if (!mat.Equals(World.INSTANCE.SelectedBlock) && !mat.Equals(World.INSTANCE.HighlightBlock))
                            DestroyImmediate(mat);
                    }
                    else
                    {
                        Destroy(mat.mainTexture);
                        if (!mat.Equals(World.INSTANCE.SelectedBlock) && !mat.Equals(World.INSTANCE.HighlightBlock))
                            Destroy(mat);
                    }
                }

                foreach (var meshFilter in tdObject.GetComponentsInChildren<MeshFilter>())
                {
                    if (immediate)
                        DestroyImmediate(meshFilter.sharedMesh);
                    else
                        Destroy(meshFilter.sharedMesh);
                }


                if (immediate)
                    DestroyImmediate(tdObject.gameObject);
                else
                    Destroy(tdObject.gameObject);

                tdObject = null;
            }

            if (tdObjectContainer != null)
            {
                DestroyImmediate(tdObjectContainer.gameObject);
                tdObjectContainer = null;
            }

            TdObjectCollider = null;
        }

        private void InitializeProps(Vector3 initialPosition, float initialScale)
        {
            var props = new TdObjectBlockProperties(Block.GetProps() as TdObjectBlockProperties)
            {
                initialPosition = new SerializableVector3(initialPosition),
                initialScale = initialScale
            };
            Block.SetProps(props, land);
        }

        private void EditProps()
        {
            var manager = GameManager.INSTANCE;
            var dialog = manager.OpenDialog();
            dialog
                .WithTitle("3D Object Properties")
                .WithContent(TdObjectBlockEditor.PREFAB);
            var editor = dialog.GetContent().GetComponent<TdObjectBlockEditor>();

            var props = Block.GetProps();
            editor.SetValue(props == null ? null : props as TdObjectBlockProperties);
            dialog.WithAction("OK", () =>
            {
                var value = editor.GetValue();
                var props = new TdObjectBlockProperties(Block.GetProps() as TdObjectBlockProperties);
                props.UpdateProps(value);

                if (props.IsEmpty()) props = null;

                Block.SetProps(props, land);
                manager.CloseDialog(dialog);
            });
        }

        private IEnumerator LoadBytes(string url, TdObjectBlockProperties.TdObjectType type,
            Action<GameObject> onSuccess)
        {
            using var webRequest = UnityWebRequest.Get(url);
            var op = webRequest.SendWebRequest();

            while (!op.isDone)
            {
                if (webRequest.downloadedBytes > DownloadLimitMb * 1000000)
                    break;
                yield return null;
            }

            switch (webRequest.result)
            {
                case UnityWebRequest.Result.InProgress:
                    UpdateState(State.SizeLimit);
                    break;
                case UnityWebRequest.Result.ConnectionError:
                    Debug.LogError($"Get for {url} caused Error: {webRequest.error}");
                    UpdateState(State.ConnectionError);
                    break;
                case UnityWebRequest.Result.DataProcessingError:
                case UnityWebRequest.Result.ProtocolError:
                    Debug.LogError($"Get for {url} caused HTTP Error: {webRequest.error}");
                    UpdateState(State.InvalidUrlOrData);
                    break;
                case UnityWebRequest.Result.Success:
                    Action onFailure = () => { UpdateState(State.InvalidData); };

                    switch (type)
                    {
                        case TdObjectBlockProperties.TdObjectType.OBJ:
                            ObjLoader.INSTANCE.InitTask(webRequest.downloadHandler.data, onSuccess, onFailure);
                            break;
                        case TdObjectBlockProperties.TdObjectType.GLB:
                            GlbLoader.InitTask(webRequest.downloadHandler.data, onSuccess, onFailure);
                            break;
                        default:
                            onFailure.Invoke();
                            break;
                    }

                    break;
            }
        }

        protected override void OnDestroy()
        {
            DestroyObject(false);
            base.OnDestroy();
        }
    }
}