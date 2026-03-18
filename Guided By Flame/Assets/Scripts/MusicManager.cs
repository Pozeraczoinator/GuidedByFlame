using UnityEngine;

public class MusicManager : MonoBehaviour
{
    public AudioSource mainMusicSource;
    public AudioSource chaseMusicSource;

    private bool isChasing = false;

    private void Start()
    {
        mainMusicSource.Play();
        chaseMusicSource.Stop();
    }

    public void StartChase()
    {
        if (isChasing) return;
        isChasing = true;

        mainMusicSource.Stop();
        chaseMusicSource.Play();
    }

    public void StopChase()
    {
        if (!isChasing) return;
        isChasing = false;

        chaseMusicSource.Stop();
        mainMusicSource.Play();
    }
}