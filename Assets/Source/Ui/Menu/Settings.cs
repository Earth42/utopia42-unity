using System.Linq;
using Source.Model;
using Source.Service.Ethereum;
using Source.Ui;
using Source.Ui.LoadingLayer;
using Source.Ui.Toaster;
using UnityEngine;
using UnityEngine.UIElements;
using Position = Source.Model.Position;

namespace Source.Canvas
{
    public class Settings : UxmlElement
    {
        private static readonly string GUEST = "guest";

        private GameObject panel;
        private TextField walletField;
        private DropdownField networkField;
        private Button submitButton;
        private Button guestButton;

        private Vector3? startingPosition = null;

        public Settings(MonoBehaviour monoBehaviour): base("Ui/Menu/Settings", true)
        {
            walletField = this.Q<TextField>("wallet");
            guestButton = this.Q<Button>("guestButton");
            guestButton.clickable.clicked += SetGuest;
            submitButton = this.Q<Button>("enterButton");
            submitButton.clickable.clicked += Submit;
            networkField = this.Q<DropdownField>("network");
            var loadingId = LoadingLayer.Show(Menu.INSTANCE.VisualElement());
            monoBehaviour.StartCoroutine(EthNetwork.GetNetworks(_ =>
                {
                    LoadingLayer.Hide(loadingId);
                    DoStart();
                },
                () =>
                {
                    ToasterService.Show("Could not load any ETHEREUM networks. Please report the error.",
                        ToasterService.ToastType.Error, null);
                    LoadingLayer.Hide(loadingId);
                }));
        }

        private void DoStart()
        {
            var nets = EthNetwork.GetNetworksIfPresent();
            if (nets == null || nets.Length == 0)
            {
                ToasterService.Show("Could not load any ETHEREUM networks. Please report the error.",
                    ToasterService.ToastType.Error, null);
                return;
            }

            networkField.choices = nets.Select(network => network.name).ToList();

            ResetInputs();
            walletField.RegisterValueChangedCallback(_ => ResetButtonsState());

            if (!WebBridge.IsPresent()) return;

            WebBridge.CallAsync<ConnectionDetail>("connectMetamask", "", (ci) =>
            {
                if (ci.network.HasValue && ci.wallet != null)
                {
                    PlayerPrefs.SetInt(Keys.NETWORK, ci.network.Value);
                    PlayerPrefs.SetString(Keys.WALLET, ci.wallet);
                    ResetInputs();
                }
            });

            guestButton.SetEnabled(false);
            submitButton.SetEnabled(false);
            WebBridge.CallAsync<Position>("getStartingPosition", "", (pos) =>
            {
                if (pos == null)
                    startingPosition = null;
                else
                    startingPosition = new Vector3(pos.x, pos.y, pos.z);
                guestButton.SetEnabled(true);
                submitButton.SetEnabled(true);
            });
        }

        private void ResetInputs()
        {
            walletField.value = IsGuest() ? null : WalletId();

            var net = Network();
            networkField.value = null;

            var nets = EthNetwork.GetNetworksIfPresent();
            for (int i = 0; i < networkField.choices.Count; i++)
            {
                if (nets[i].Equals(net))
                {
                    networkField.index = i;
                    break;
                }
            }

            var serviceInitialized = EthereumClientService.INSTANCE.IsInited();
            networkField.SetEnabled(!serviceInitialized);
        }

        private void ResetButtonsState()
        {
            submitButton.SetEnabled(!string.IsNullOrEmpty(walletField.text));
        }

        void Update()
        {
            ResetButtonsState();
        }

        public void SetGuest()
        {
            DoSubmit(GUEST);
        }

        public void Submit()
        {
            if (string.IsNullOrWhiteSpace(walletField.text)) return;
            DoSubmit(walletField.text);
        }


        private void DoSubmit(string walletId)
        {
            if (networkField.enabledSelf)
                PlayerPrefs.SetInt(Keys.NETWORK, EthNetwork.GetNetworksIfPresent()[networkField.index].id);
            else if (Equals(WalletId(), walletId))
            {
                GameManager.INSTANCE.ExitSettings(startingPosition);
                return;
            }

            PlayerPrefs.SetString(Keys.WALLET, walletId);
            GameManager.INSTANCE.SettingsChanged(Network(), startingPosition);
        }

        public static bool IsGuest()
        {
            return WalletId().Equals(GUEST);
        }

        public static string WalletId()
        {
            return PlayerPrefs.GetString(Keys.WALLET).ToLower();
        }

        private static EthNetwork Network()
        {
            var nets = EthNetwork.GetNetworksIfPresent();
            return EthNetwork.GetByIdIfPresent(PlayerPrefs.GetInt(Keys.NETWORK,
                nets == null || nets.Length == 0 ? -1 : nets[0].id));
        }

        public static ConnectionDetail ConnectionDetail()
        {
            var detail = new ConnectionDetail();
            detail.wallet = WalletId();
            var net = Network();
            detail.network = net?.id ?? -1;
            return detail;
        }
    }

    class Keys
    {
        public static readonly string WALLET = "WALLET";
        public static readonly string NETWORK = "NETWORK";
    }
}