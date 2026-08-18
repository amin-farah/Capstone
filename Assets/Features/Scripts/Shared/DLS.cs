using UnityEngine;

public enum SoundLocation
{
    LeftEar = -1,
    RightEar = 1
}

public static class DLS 
{
    
    private static void ConfigureAudio(AudioSource audioSource, SoundLocation location)
    {
        audioSource.spatialBlend = 0f; // Used to disable audio in a 3D environment in case turned 
        audioSource.panStereo = (float) location; //Set location of audio source to a given ear
    }
    
    public static void PassAudio(AudioSource audioSource, SoundLocation location, AudioClip audioClip)
    {      
        ConfigureAudio(audioSource, location);
        audioSource.clip = audioClip;
        audioSource.Play();
    }
    
    public static double PlayDichotic(AudioSource leftEar, AudioSource rightEar, AudioClip leftEarSound, AudioClip rightEarSound, float leadIn = 0.1f, float rightOffset = 0.0f)
    {
        ConfigureAudio(leftEar, SoundLocation.LeftEar);
        ConfigureAudio(rightEar, SoundLocation.RightEar);
        leftEar.clip = leftEarSound; //Assign the sounds to their correct ears
        rightEar.clip = rightEarSound;

        double t = AudioSettings.dspTime + leadIn; //Schedule sounds to avoid any buffering issues
        leftEar.PlayScheduled(t);
        rightEar.PlayScheduled(t + rightOffset);
        return t;
    }
}