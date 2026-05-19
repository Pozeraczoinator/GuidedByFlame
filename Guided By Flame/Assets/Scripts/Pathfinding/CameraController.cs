using UnityEngine;

namespace Pathfinding.Visualization
{
    [RequireComponent(typeof(Camera))]
    public class CameraController : MonoBehaviour
    {
        [Header("Autosizing")]
        public float padding = 2f;
        
        [Header("Controls")]
        public float zoomSpeed = 2f;
        public float minZoom = 2f;
        public float maxZoom = 200f;

        private Camera _cam;
        private Vector3 _dragOrigin;

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            if (!_cam.orthographic)
            {
                Debug.LogWarning("[CameraController] Camera must be orthographic for 2D visualization.");
            }
            _cam.backgroundColor = Color.black;
        }

        /// <summary>
        /// Dostosowuje rozmiar i pozycje kamery tak, aby cała mapa mieściła się na ekranie.
        /// </summary>
        public void AutoSizeToMap(int mapWidth, int mapHeight)
        {
            if (_cam == null || !_cam.orthographic) return;

            // Środek mapy (od 0 do width-1)
            float centerX = (mapWidth - 1) / 2f;
            float centerY = (mapHeight - 1) / 2f;
            
            transform.position = new Vector3(centerX, centerY, -10f);

            float targetOrthographicSize = (mapHeight / 2f) + padding;
            float screenRatio = (float)Screen.width / (float)Screen.height;
            float targetRatio = (mapWidth + padding * 2) / (mapHeight + padding * 2);

            // Jeśli ekran jest węższy niż proporcje mapy, musimy zwiększyć rozmiar kamery
            if (screenRatio >= targetRatio)
            {
                _cam.orthographicSize = targetOrthographicSize;
            }
            else
            {
                float differenceInSize = targetRatio / screenRatio;
                _cam.orthographicSize = targetOrthographicSize * differenceInSize;
            }
        }

        private void Update()
        {
            HandleZoom();
            HandleDrag();
        }

        private void HandleZoom()
        {
            if (!_cam.orthographic) return;
            
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0.0f)
            {
                // Skalowanie proporcjonalne do obecnego przybliżenia
                _cam.orthographicSize -= scroll * zoomSpeed * _cam.orthographicSize;
                _cam.orthographicSize = Mathf.Clamp(_cam.orthographicSize, minZoom, maxZoom);
            }
        }

        private void HandleDrag()
        {
            // Przesuwanie kamery za pomocą Prawego (1) lub Środkowego (2) przycisku myszy
            if (Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
            {
                _dragOrigin = _cam.ScreenToWorldPoint(Input.mousePosition);
            }

            if (Input.GetMouseButton(1) || Input.GetMouseButton(2))
            {
                Vector3 difference = _dragOrigin - _cam.ScreenToWorldPoint(Input.mousePosition);
                transform.position += difference;
            }
        }
    }
}
