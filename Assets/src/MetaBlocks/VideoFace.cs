using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Video;

namespace src
{
    public class VideoFace : MetaFace
    {
        private VideoPlayer videoPlayer;
        public readonly UnityEvent<bool> loading = new UnityEvent<bool>();
        private float prevTime;
        private bool previewing = true;
        private bool prepared = false;

        public void Init(MeshRenderer meshRenderer, string url, float prevTime)
        {
            previewing = true;
            prepared = false;
            loading.Invoke(true);
            this.prevTime = prevTime;
            videoPlayer = gameObject.AddComponent<VideoPlayer>();
            videoPlayer.url = url;
            videoPlayer.playOnAwake = false;
            videoPlayer.Pause();
            videoPlayer.Prepare();
            videoPlayer.prepareCompleted += PrepareCompeleted;
            meshRenderer.sharedMaterial.mainTexture = videoPlayer.texture;
        }

        private void Mute(bool m)
        {
            for (int i = 0; i < videoPlayer.length; i++)
                videoPlayer.SetDirectAudioMute((ushort) i, m);
        }

        private void PrepareCompeleted(VideoPlayer vp)
        {
            StartCoroutine(Seek());
            videoPlayer.prepareCompleted -= PrepareCompeleted;
        }

        private IEnumerator Seek()
        {
            Mute(true);
            yield return null;
            videoPlayer.time = prevTime;
            videoPlayer.Play();
            yield return null;

            while (videoPlayer.time < prevTime + 0.01 && videoPlayer.time > prevTime - 0.01)
                yield return null;
            videoPlayer.Pause();
            yield return null;
            Mute(false);
            prepared = true;
            loading.Invoke(false);
        }


        private IEnumerator DoOnNext(UnityAction a)
        {
            yield return null;
            a.Invoke();
        }

        public void TogglePlaying()
        {
            if (!prepared) return;

            if (videoPlayer.isPlaying)
                videoPlayer.Pause();
            else
            {
                if (previewing)
                {
                    videoPlayer.time = 0;
                    previewing = false;
                }

                videoPlayer.Play();
            }
        }

        public bool IsPrepared()
        {
            return prepared && videoPlayer.isPrepared;
        }

        public bool IsPlaying()
        {
            return prepared && videoPlayer.isPlaying;
        }

        private void OnDestroy()
        {
            videoPlayer.Stop();
            Destroy(videoPlayer.texture);
            base.OnDestroy();
        }
    }
}