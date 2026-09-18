using System;
using CamLinkPro.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CamLinkPro.Pairing
{
    /// <summary>Manual-entry fallback for pairing (IP + both ports + token), in
    /// case QR scanning isn't practical in a given room.</summary>
    public sealed class ManualPairingEntry : MonoBehaviour
    {
        [SerializeField] InputField ipField;
        [SerializeField] InputField posePortField;
        [SerializeField] InputField videoPortField;
        [SerializeField] InputField tokenField;
        [SerializeField] Text errorText;

        public event Action<PairingInfo> OnPaired;

        /// <summary>Fills the fields from a pairing info that came from
        /// elsewhere (a QR scan, or the last successful pairing) -- lets the
        /// user review/correct a single field (e.g. the video port not matching
        /// what's actually configured in the Blender add-on) instead of retyping
        /// everything from scratch.</summary>
        public void Prefill(PairingInfo info)
        {
            if (ipField != null) ipField.text = info.Ip ?? "";
            if (posePortField != null) posePortField.text = info.PosePort > 0 ? info.PosePort.ToString() : "";
            if (videoPortField != null) videoPortField.text = info.VideoPort > 0 ? info.VideoPort.ToString() : "";
            if (tokenField != null) tokenField.text = info.Token ?? "";
        }

        public void SubmitFromFields()
        {
            string ip = ipField != null ? ipField.text.Trim() : "";
            string token = tokenField != null ? tokenField.text.Trim() : "";
            bool okPose = int.TryParse(posePortField != null ? posePortField.text : "", out int posePort);
            bool okVideo = int.TryParse(videoPortField != null ? videoPortField.text : "", out int videoPort);

            var info = new PairingInfo { Ip = ip, PosePort = posePort, VideoPort = videoPort, Token = token };

            if (string.IsNullOrEmpty(ip) || !okPose || !okVideo || string.IsNullOrEmpty(token) || !info.IsValid)
            {
                SetError("Enter a valid IP, both ports (1-65535), and the token from the pairing QR.");
                return;
            }

            SetError(null);
            OnPaired?.Invoke(info);
        }

        void SetError(string message)
        {
            if (errorText == null) return;
            errorText.text = message ?? "";
            errorText.gameObject.SetActive(!string.IsNullOrEmpty(message));
        }
    }
}
