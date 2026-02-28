using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    [SerializeField] private GameObject HudMenu;
    [SerializeField] private GameObject PauseMenu;
    [SerializeField] private int MainMenuIndex;

    [SerializeField] private GameObject ParticlesHolder;

    [SerializeField] private Color EnabledColor;
    [SerializeField] private Color DisabledColor;
    [SerializeField] private float ShadowColorOffset;

    [SerializeField] private string MusicMixerVolumeName;
    [SerializeField] private string SfxMixerVolumeName;

     
    private void ShowPauseMenu()
    {
        HudMenu.SetActive(false);
        PauseMenu.SetActive(true);
    }
    public void ShowHudMenu()
    {
        HudMenu.SetActive(true);
        PauseMenu.SetActive(false);
    }

    public void PauseGame()
    {
        ShowPauseMenu();
        Time.timeScale = 0;
    }
    public void ResumeGame()
    {
        ShowHudMenu();
        Time.timeScale = 1;
    }
    public void QuitGame() => Application.Quit();
    public void ReturnToMainMenu()
    {
        Time.timeScale = 1;
        SceneManager.LoadScene(MainMenuIndex);
    }
    
    public void ToggleVfx(Image button)
    {
        var isCurrentlyActive = ParticlesHolder.activeSelf;
        ParticlesHolder.SetActive(!isCurrentlyActive);
        
        button.color = !isCurrentlyActive ? EnabledColor : DisabledColor;
        button.transform.GetComponent<Shadow>().effectColor = button.color - new Color(ShadowColorOffset, ShadowColorOffset, ShadowColorOffset, 0);
    }
    public void ToggleMusic(Image button)
    {
        var mixerVolume = AudioManager.Instance.GetMixerVolume(MusicMixerVolumeName);
        var isCurrentlyActive = !Mathf.Approximately(mixerVolume, -80);
        
        button.color = !isCurrentlyActive ? EnabledColor : DisabledColor;
        button.transform.GetComponent<Shadow>().effectColor = button.color - new Color(ShadowColorOffset, ShadowColorOffset, ShadowColorOffset, 0);

        if (isCurrentlyActive) AudioManager.Instance.SetMixerVolume(MusicMixerVolumeName, 0, new Vector2(-80, 20));
        else
        {
            var normalVolume = AudioManager.ConvertRange(
                0,
                new Vector2(-80, 20),
                new Vector2(0, 1));

            AudioManager.Instance.SetMixerVolume(
                MusicMixerVolumeName, normalVolume, new Vector2(-80, 20));
        }
    }
    public void ToggleSfx(Image button)
    {
        var mixerVolume = AudioManager.Instance.GetMixerVolume(SfxMixerVolumeName);
        var isCurrentlyActive = !Mathf.Approximately(mixerVolume, -80);
        
        button.color = !isCurrentlyActive ? EnabledColor : DisabledColor;
        button.transform.GetComponent<Shadow>().effectColor = button.color - new Color(ShadowColorOffset, ShadowColorOffset, ShadowColorOffset, 0);

        if (isCurrentlyActive) AudioManager.Instance.SetMixerVolume(SfxMixerVolumeName, 0, new Vector2(-80, 20));
        else
        {
            var normalVolume = AudioManager.ConvertRange(
                0,
                new Vector2(-80, 20),
                new Vector2(0, 1));

            AudioManager.Instance.SetMixerVolume(
                SfxMixerVolumeName, normalVolume, new Vector2(-80, 20));
        } 
    }

    public void TogglePostProcessing(Image button)
    {
        var cameraData = GameManager.Instance.PlayerCamera.GetUniversalAdditionalCameraData();
        var isCurrentlyActive = cameraData.renderPostProcessing;
        
        cameraData.renderPostProcessing = !isCurrentlyActive;
        button.color = !isCurrentlyActive ? EnabledColor : DisabledColor;
        button.transform.GetComponent<Shadow>().effectColor = button.color - new Color(ShadowColorOffset, ShadowColorOffset, ShadowColorOffset, 0);
    }
}
