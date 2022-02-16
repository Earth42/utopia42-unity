using src;
using src.Canvas;
using src.Model;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace src.Canvas.Map
{
    public class LandView : MonoBehaviour
    {
        private Land land;

        public TextMeshProUGUI landIdLabel;
        public TextMeshProUGUI coordinateLabel;
        public TextMeshProUGUI sizeLabel;
        public GameObject nftToggle;
        public Button button;

        private void Start()
        {
            button.onClick.AddListener(() => GameManager.INSTANCE.NavigateInMap(land));
        }

        public void SetLand(Land land)
        {
            this.land = land;
            landIdLabel.SetText("#" + land.id);
            coordinateLabel.SetText("(" + land.startCoordinate.ToVector3() + " - " +
                                    land.endCoordinate.ToVector3() + ")");
            sizeLabel.SetText(GetLandSize(land).ToString());
            nftToggle.SetActive(land.isNft);
        }

        private long GetLandSize(Land land1)
        {
            var rect = land.ToRect();
            return (long) (rect.width * rect.height);
        }
    }
}