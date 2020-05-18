﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using System.Diagnostics;

public class ISF : MonoBehaviour
{
    public FFT fft;
    public ComputeShader ISFCS;

    RenderTexture SchroedingerMul;
    RenderTexture PossionMul;
    RenderTexture Velocity;
    RenderTexture Divergence;
    RenderTexture TempRT;

    int kernelInitBuffer;
    int kernelShoedinger;
    int kernelNormalize;
    int kernelVelocityOneForm;
    int kernelComputeDivergence;
    int kernelPossionSpectral;
    int kernelGaugeTransform;


    int N = 16;

    Vector3 size = new Vector3(2, 2, 2);
    float hbar = 0.1f;
    float estimate_dt = 1f / 30f;

    

    

    // Start is called before the first frame update
    void Start()
    {
        fft.init();
        fft.myRunTest();

        InitComputeShader();
        InitISF();

        MyRunTest();
    }

    void DispatchISFCS(int kernel)
    {
        ISFCS.Dispatch(kernel, N / 8, N / 8, N / 8);
    }

    void InitComputeShader()
    {
        kernelInitBuffer = ISFCS.FindKernel("InitBuffer");
        kernelShoedinger = ISFCS.FindKernel("Shoedinger");
        kernelNormalize = ISFCS.FindKernel("Normalize");

        kernelVelocityOneForm = ISFCS.FindKernel("VelocityOneForm");
        kernelComputeDivergence = ISFCS.FindKernel("ComputeDivergence");
        kernelPossionSpectral = ISFCS.FindKernel("PossionSpectral");
        kernelGaugeTransform = ISFCS.FindKernel("GaugeTransform");
    }

    void InitISF()
    {
        int[] res = { N, N, N };

        SchroedingerMul = FFT.CreateRenderTexture3D(N, N, N, RenderTextureFormat.RGFloat);
        PossionMul = FFT.CreateRenderTexture3D(N, N, N, RenderTextureFormat.RFloat);
        Velocity = FFT.CreateRenderTexture3D(N, N, N, RenderTextureFormat.ARGBFloat);
        Divergence = FFT.CreateRenderTexture3D(N, N, N, RenderTextureFormat.RGFloat);
        TempRT = FFT.CreateRenderTexture3D(N, N, N, RenderTextureFormat.RGFloat);

        fft.OutputRT = TempRT;
        fft.SetN(N);

        ISFCS.SetVector("size", size);
        ISFCS.SetInts("res", res);
        ISFCS.SetFloat("hbar", hbar);
        ISFCS.SetFloat("dt", estimate_dt);

        ISFCS.SetTexture(kernelInitBuffer, "SchroedingerMul", SchroedingerMul);
        ISFCS.SetTexture(kernelInitBuffer, "PossionMul", PossionMul);
        DispatchISFCS(kernelInitBuffer);

        fft.fftshift(ref SchroedingerMul, ref TempRT);
    }

    void ShroedingerIntegration(ref RenderTexture psi1, ref RenderTexture psi2)
    {
        fft.fft(ref psi1, ref TempRT);
        fft.fft(ref psi2, ref TempRT);
        
        ISFCS.SetTexture(kernelShoedinger, "Psi1", psi1);
        ISFCS.SetTexture(kernelShoedinger, "Psi2", psi2);
        ISFCS.SetTexture(kernelShoedinger, "SchroedingerMul", SchroedingerMul);
        DispatchISFCS(kernelShoedinger);

        

        fft.ifft(ref psi1, ref TempRT);
        fft.ifft(ref psi2, ref TempRT);
    }

    void Normalize(ref RenderTexture psi1, ref RenderTexture psi2)
    {
        ISFCS.SetTexture(kernelNormalize, "Psi1", psi1);
        ISFCS.SetTexture(kernelNormalize, "Psi2", psi2);
        DispatchISFCS(kernelNormalize);
    }

    void PressureProject(ref RenderTexture psi1, ref RenderTexture psi2)
    {
        ISFCS.SetFloat("hbar", 1);
        // 首先计算 Oneform
        ISFCS.SetTexture(kernelVelocityOneForm, "Psi1", psi1);
        ISFCS.SetTexture(kernelVelocityOneForm, "Psi2", psi2);
        ISFCS.SetTexture(kernelVelocityOneForm, "Velocity", Velocity);
        DispatchISFCS(kernelVelocityOneForm);

        ISFCS.SetFloat("hbar", hbar);
        // fft.ExportFloat4_3D(Velocity, "test/isf.velo.json");

        // 计算散度
        ISFCS.SetTexture(kernelComputeDivergence, "Velocity", Velocity);
        ISFCS.SetTexture(kernelComputeDivergence, "Divergence", Divergence);
        DispatchISFCS(kernelComputeDivergence);

        //fft.ExportComplex3D(Divergence, "test/isf.div.json");

        // 求解 Possion 方程
        fft.fft(ref Divergence, ref TempRT);
        // Divergence 比较大, FFT 之后会放大 Divergence, 
        // float & double 会产生超过 0.02 的精度误差

        ISFCS.SetTexture(kernelPossionSpectral, "PossionMul", PossionMul);
        ISFCS.SetTexture(kernelPossionSpectral, "Divergence", Divergence);
        DispatchISFCS(kernelPossionSpectral);
        
        fft.ifft(ref Divergence, ref TempRT);
        //fft.ExportComplex3D(Divergence, "test/isf.pos.json");

        //fft.ExportFloat1_3D(PossionMul, "test/isf.fac.json");

        ISFCS.SetTexture(kernelGaugeTransform, "Psi1", psi1);
        ISFCS.SetTexture(kernelGaugeTransform, "Psi2", psi2);
        ISFCS.SetTexture(kernelGaugeTransform, "Divergence", Divergence);
        DispatchISFCS(kernelGaugeTransform);
    }

    void MyRunTest()
    {
        Texture3D tex_psi1;
        Texture3D tex_psi2;

        var psi1 = fft.LoadJson3D("test/psi1.json", out tex_psi1);
        var psi2 = fft.LoadJson3D("test/psi2.json", out tex_psi2);

        fft.ExportComplex3D(psi1, "test/sch.ps1.fft.json");

        ShroedingerIntegration(ref psi1, ref psi2);
        

        fft.ExportArray(SchroedingerMul, TextureFormat.RGBAFloat, 2, "test/isf.sch.mul.json");

        fft.ExportArray(psi1, TextureFormat.RGBAFloat, 2, "test/isf.sch.ps1.json");
        fft.ExportArray(psi2, TextureFormat.RGBAFloat, 2, "test/isf.sch.ps2.json");

        Normalize(ref psi1, ref psi2);

        
        fft.ExportArray(psi1, TextureFormat.RGBAFloat, 2, "test/isf.nor.ps1.json");
        fft.ExportArray(psi2, TextureFormat.RGBAFloat, 2, "test/isf.nor.ps2.json");

        PressureProject(ref psi1, ref psi2);

        fft.ExportArray(psi1, TextureFormat.RGBAFloat, 2, "test/isf.pre.ps1.json");
        fft.ExportArray(psi2, TextureFormat.RGBAFloat, 2, "test/isf.pre.ps2.json");

        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}