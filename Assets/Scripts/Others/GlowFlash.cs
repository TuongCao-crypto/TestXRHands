using System;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class GlowFlashLoop : MonoBehaviour
{
    private List<Renderer> targetRenderers;
    public Color glowColor = Color.yellow;
    public float glowIntensity = 5f;
    public float flashDuration = 0.5f;

    private List<Material> _mats;
    [SerializeField] bool _useEmission = true;

    private Tween glowTween = null;
    private Color originalEmissionColor;
    private float originalMetallic;
    [SerializeField] private bool runFromStart = true;

    void Start()
    {
        targetRenderers = new List<Renderer>(GetComponentsInChildren<Renderer>());
        if (targetRenderers != null)
        {
            _mats = new List<Material>(targetRenderers.Count);
            for (int i = 0; i < targetRenderers.Count; i++)
            {
                _mats.Add(targetRenderers[i].material);
                _mats[i].EnableKeyword("_EMISSION");
            }

            // Enable emission
        }


        if (runFromStart) StartGlowLoop();
    }

    public void StartGlowLoop()
    {
        if (glowTween != null && glowTween.IsActive())
        {
            glowTween.Kill();
            glowTween = null;
        }

        if (_useEmission)
        {
            originalEmissionColor = _mats[0].GetColor("_EmissionColor");
            glowTween = DOTween.To(
                    () => 0f,
                    x =>
                    {
                        foreach (var _mat in _mats)
                        {
                            _mat.SetColor("_EmissionColor", glowColor * x);
                        }
                       
                    },
                    glowIntensity,
                    flashDuration
                )
                .SetLoops(-1, LoopType.Yoyo) // Infinite loop, back and forth
                .SetEase(Ease.InOutSine)
                .SetDelay(1);
        }
        else
        {
            originalMetallic = _mats[0].GetFloat("_Metallic");
            glowTween = DOTween.To(
                    () => 0f,
                    x =>
                    {
                        foreach (var _mat in _mats)
                        {
                            _mat.SetFloat("_Metallic", x);
                        }
                       
                    },
                    1f, // Target value (from 0 to 1)
                    flashDuration
                )
                .SetLoops(-1, LoopType.Yoyo) // Infinite loop, back and forth
                .SetEase(Ease.InOutSine)
                .SetDelay(1);
        }
    }

    public void StopGlowLoop()
    {
        if (glowTween != null && glowTween.IsActive())
        {
            glowTween.Kill();
            glowTween = null;
        }

        foreach (var _mat in _mats)
        {
            if (_useEmission)
            {
                _mat.SetColor("_EmissionColor", originalEmissionColor);
            }
            else
            {
                _mat.SetFloat("_Metallic", originalMetallic);
            }
        }
        
    }
}