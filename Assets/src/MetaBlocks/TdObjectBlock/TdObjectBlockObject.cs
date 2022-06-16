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

        private GameObject objContainer;
        public GameObject Obj { private set; get; }

        private Collider objCollider;

        private TdObjectFocusable objFocusable;

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
            if (objCollider == null) return;
            if (objCollider is BoxCollider boxCollider)
                AdjustHighlightBox(Player.INSTANCE.tdObjectHighlightBox, boxCollider, true);
            else
            {
                Player.INSTANCE.RemoveHighlightMesh();
                Player.INSTANCE.focusHighlight = CreateMeshHighlight(World.INSTANCE.HighlightBlock);
            }
        }

        public override void RemoveFocusHighlight()
        {
            if (Player.INSTANCE.RemoveHighlightMesh()) return;
            Player.INSTANCE.tdObjectHighlightBox.gameObject.SetActive(false);
        }

        public override GameObject CreateSelectHighlight(Transform parent, bool show = true)
        {
            if (objCollider == null) return null;

            Transform highlight;

            if (objCollider is not BoxCollider boxCollider)
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
            var go = objCollider.gameObject;
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
            if (objContainer == null || State != State.Ok) return;
            props.rotation = new SerializableVector3(objContainer.transform.eulerAngles);
            props.scale = new SerializableVector3(objContainer.transform.localScale);
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
                    UnFocus();
                    EditProps();
                }

                if (Input.GetKeyDown(KeyCode.V) && State == State.Ok)
                {
                    UnFocus();
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
                scaleRotationController.Attach(objContainer.transform, objContainer.transform);
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
            if (state == State.Ok)
            {
                if (Block.IsCursor && objCollider != null)
                    DestroyImmediate(objCollider);

                var newMinGlobalY = GetMinGlobalY(Obj);
                if (Block.IsCursor)
                {
                    var delta = newMinGlobalY - MinGlobalY;
                    if (Mathf.Abs(delta) > 0.001)
                        DeltaY = newMinGlobalY - MinGlobalY;
                }

                MinGlobalY = newMinGlobalY;

                return;
            }

            // setting place holder
            DestroyObject();
            ResetContainer();
            Obj = Block.type.CreatePlaceHolder(MetaBlockState.IsErrorState(state), !Block.IsCursor);
            Obj.transform.SetParent(objContainer.transform, false);
            Obj.SetActive(true);
            objCollider = Obj.GetComponentInChildren<Collider>();
            Obj.transform.SetParent(objContainer.transform, false);

            MinGlobalY = GetMinGlobalY(Obj);
            if (chunk == null || objCollider == null) return;
            objFocusable = objCollider.gameObject.AddComponent<TdObjectFocusable>();
            objFocusable.Initialize(this);
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
            var p = (TdObjectBlockProperties) Block.GetProps();
            if (p == null)
            {
                UpdateState(State.Empty);
                return;
            }

            var scale = p.scale?.ToVector3() ?? Vector3.one;
            var rotation = p.rotation?.ToVector3() ?? Vector3.zero;
            var initialPosition = p.initialPosition?.ToVector3() ?? Vector3.zero;

            if (currentUrl.Equals(p.url) && State == State.Ok)
            {
                LoadGameObject(scale, rotation, initialPosition, p.initialScale,
                    p.detectCollision, p.type, false, null);
            }
            else
            {
                UpdateState(State.Loading);
                var reinitialize = !currentUrl.Equals("") || p.initialScale == 0;
                currentUrl = p.url;

                var go = TdObjectCache.GetAsset(this);
                if (go != null)
                {
                    LoadGameObject(scale, rotation, initialPosition, p.initialScale,
                        p.detectCollision, p.type, reinitialize, go);
                }
                else
                {
                    StartCoroutine(LoadBytes(p.url, p.type, loadedGo =>
                    {
                        LoadGameObject(scale, rotation, initialPosition, p.initialScale,
                            p.detectCollision, p.type, reinitialize, loadedGo);
                    }));
                }
            }
        }

        private void SetPlaceHolder(bool error)
        {
        }

        private void ResetContainer()
        {
            objContainer = new GameObject("3d object container");
            objContainer.transform.SetParent(transform, false);
            objContainer.transform.localPosition = Vector3.zero;
            objContainer.transform.localScale = Vector3.one;
            objContainer.transform.eulerAngles = Vector3.zero;
        }

        private void SetMeshCollider(Transform colliderTransform)
        {
            Destroy(objFocusable);
            Destroy(objCollider);

            objCollider = TdObjectTools.PrepareMeshCollider(colliderTransform);
            if (chunk != null)
            {
                objFocusable = objCollider.gameObject.AddComponent<TdObjectFocusable>();
                objFocusable.Initialize(this);
            }
        }

        private void LoadGameObject(Vector3 scale, Vector3 rotation, Vector3 initialPosition,
            float initialScale, bool detectCollision, TdObjectBlockProperties.TdObjectType type,
            bool reinitialize, GameObject loadedGo)
        {
            if (loadedGo != null)
            {
                DestroyObject();
                objCollider = null;
                ResetContainer();
                Obj = loadedGo;
                Obj.transform.localPosition = Vector3.zero;
                Obj.transform.localScale = Vector3.one;
            }

            if (objCollider == null)
            {
                objCollider = Obj.AddComponent<BoxCollider>();
                if (chunk != null)
                {
                    objFocusable = Obj.AddComponent<TdObjectFocusable>();
                    objFocusable.Initialize(this);
                }

                ((BoxCollider) objCollider).center = TdObjectTools.GetRendererCenter(Obj);
                ((BoxCollider) objCollider).size =
                    TdObjectTools.GetRendererSize(((BoxCollider) objCollider).center, Obj);
                Obj.transform.SetParent(objContainer.transform, false);
            }

            if (reinitialize)
            {
                Obj.transform.localScale = Vector3.one;
                Obj.transform.localPosition = Vector3.zero;

                var size = ((BoxCollider) objCollider).size;
                var maxD = new[] {size.x, size.y, size.z}.Max();
                var newInitScale = maxD > 10f ? 10f / maxD : 1;
                Obj.transform.localScale = newInitScale * Vector3.one;
                var newInitPosition = objContainer.transform.TransformPoint(Vector3.zero) -
                                      objCollider.transform.TransformPoint(((BoxCollider) objCollider)
                                          .center);

                if (Math.Abs(newInitScale - initialScale) > 0.0001 || newInitPosition != initialPosition)
                {
                    InitializeProps(newInitPosition, newInitScale);
                    return;
                }
            }

            Obj.transform.localScale = initialScale * Vector3.one;
            Obj.transform.localPosition = initialPosition;

            objContainer.transform.localScale = scale;
            objContainer.transform.eulerAngles = rotation;

            var colliderTransform = TdObjectTools.GetMeshColliderTransform(Obj);
            if (colliderTransform != null && type == TdObjectBlockProperties.TdObjectType.GLB)
            {
                // replace box collider with mesh collider if any colliders are defined in the glb object
                if (objCollider is BoxCollider)
                    SetMeshCollider(colliderTransform);

                if (Block.land != null && !InLand(objCollider.GetComponent<MeshRenderer>()))
                {
                    UpdateState(State.OutOfBound);
                    return;
                }
            }
            else if (Block.land != null && !InLand((BoxCollider) objCollider))
            {
                UpdateState(State.OutOfBound);
                return;
            }

            objCollider.gameObject.layer =
                detectCollision ? LayerMask.NameToLayer("Default") : LayerMask.NameToLayer("3DColliderOff");

            UpdateState(State.Ok);
            TdObjectCache.Add(this);
            // chunk.UpdateMetaHighlight(new VoxelPosition(Vectors.FloorToInt(transform.position))); // TODO: fix on focus
        }

        private void DestroyObject(bool immediate = true)
        {
            if (objFocusable != null)
            {
                objFocusable.UnFocus();
                objFocusable = null;
            }

            if (Obj != null)
            {
                DeepDestroy3DObject(Obj, immediate);
                Obj = null;
            }

            if (objContainer != null)
            {
                DestroyImmediate(objContainer.gameObject);
                objContainer = null;
            }

            objCollider = null;
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