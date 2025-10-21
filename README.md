# Tsunami Visualizer — Unity (Cliente 3D)

> **Unity**: 6000.0.43f1 · **Pipeline**: URP · **Cesium for Unity**: 1.x (Ion)

![demo](docs/demo.gif)

## Tabla de contenido

* [Descripción](#descripción)
* [Arquitectura (lado Unity)](#arquitectura-lado-unity)
* [Requisitos](#requisitos)
* [Instalación](#instalación)
* [Configuración de Cesium](#configuración-de-cesium)
* [Datos de simulación](#datos-de-simulación)
* [Estructura del proyecto](#estructura-del-proyecto)
* [Shaders y materiales](#shaders-y-materiales)
* [Scripts principales](#scripts-principales)
* [Controles de simulación](#controles-de-simulación)
* [Flujo de trabajo](#flujo-de-trabajo)
* [Rendimiento](#rendimiento)
* [Solución de problemas](#solución-de-problemas)
* [Roadmap Unity](#roadmap-unity)
* [Backend (referencia rápida)](#backend-referencia-rápida)
* [Licencia](#licencia)

---

## Descripción

Visualizador 3D de propagación de tsunami sobre terreno geoespacial. El cliente Unity consume **frames de altura** (heightmaps) para representar el avance de la inundación/ola en el tiempo. Se apoya en **Cesium for Unity** para el globo/terreno y en un **shader URP** que desplaza vértices de un mesh de agua mediante una **Texture2DArray (R16)**.

**Objetivo del cliente**: entregar una experiencia interactiva con **timeline** (play/pausa/reinicio/scrub), visualmente clara (coloración del agua, posibilidad de espuma y fresnel) y con escalado de alturas en **metros**.

> ⚠️ Este README se centra solo en **Unity**. El backend (FastAPI + MinIO) se menciona al final como contexto.

---

## Arquitectura (lado Unity)

```
Cesium Globe (Terrain/Tiles) + City Meshes
           │
           ├── Water Mesh (plane subdividido o grid dinámico)
           │     └── Material "Tsunami/TsunamiDisplace16_Array_URP"
           │            - _HeightArray (Texture2DArray R16)
           │            - _Slice (índice frame)
           │            - _DispScale (metros de desplazamiento)
           │
           └── UI Timeline (Slider + Botones)
                 - TsunamiTimelineController.cs
                 - Formateo de tiempo (hh:mm:ss)
```

---

## Requisitos

* **Unity 6000.0.43f1** (o compatible 6000.x)
* **Universal Render Pipeline (URP)** instalado en el proyecto
* **Cesium for Unity** (con token de **Cesium Ion**)
* Plataforma objetivo: Editor / Standalone / (experimental) WebGL

### Ajustes recomendados

* **Color Space**: Linear
* **Graphics API**: DX11/Metal/Vulkan (según plataforma)
* **URP**: Asset configurado; Quality en nivel medio/alto para teselar/filtrar correctamente

---

## Instalación

1. Clona este repo y abre el **Proyecto Unity** en 6000.0.43f1.
2. Importa **URP** (si no está) y asigna el URP Asset en *Project Settings → Graphics*.
3. Importa **Cesium for Unity** desde Package Manager.
4. Crea un **CesiumGeoreference** y una **Cesium3DTileset** según tu región (Valparaíso u otra).
5. Abre la escena de ejemplo: `Assets/Scenes/WorldParadise_3.unity` (o la escena principal del proyecto).

---

## Configuración de Cesium

* Agrega tu **Cesium Ion Access Token** en *Cesium → Settings*.
* Inserta un **CesiumGlobeAnchor** en los objetos que deban estar georreferenciados.
* Ajusta el punto de interés (lat/lon/altura) de la escena para centrar la cámara sobre tu zona.

> Para datasets urbanos, puedes añadir tilesets adicionales (edificios/mesh) y asociarlos al **CesiumGeoreference**.

---

## Datos de simulación

El cliente trabaja con **secuencias de heightmaps** empaquetadas en una **Texture2DArray (R16)**. Cada *slice* es un frame temporal.

* **Resolución típica**: NxN (definida por el pipeline de datos)
* **Formato**: R16 (16-bit single channel). Importar como **RFloat/Single Channel** y **no sRGB**.
* **Rango**: Normalizado por el pipeline de ingest (ver backend). El shader reescala con `_DispScale`.
* **Índices**: Se asume por defecto **1..81** (ejemplo), con un mapeo a tiempo simulado (p.ej., frame 1 ≈ 00:02:03, frame 80 ≈ 06:00:00, frame 81 ≈ 40:30:00). Este mapeo puede configurarse en el controller.

> El *loader* en Unity puede crear el `Texture2DArray` desde texturas individuales o cargar un asset preempaquetado.

---

## Estructura del proyecto

```
Assets/
  Art/
    Shaders/
      TsunamiDisplace16_Array_URP.shader
    Materials/
      Water_TsunamiArray.mat
  Scripts/
    Timeline/
      TsunamiTimelineController.cs
      TimeFormatter.cs
    Rendering/
      HeightArrayLoader.cs
  Scenes/
    WorldParadise_3.unity
  UI/
    Prefabs/
      TimelineCanvas.prefab
```

---

## Shaders y materiales

**Shader**: `Tsunami/TsunamiDisplace16_Array_URP`

* `_HeightArray (2DArray R16)` – Secuencia temporal de heightmaps.
* `_Slice (Float)` – Índice de frame actual.
* `_DispScale (Float, m)` – Escala en **metros** del desplazamiento vertical.
* Parámetros de apariencia (color base, transparencia, etc.) según versión del shader.

**Ejemplo (C#) para mover el slice**:

```csharp
// asumiendo ref a material
material.SetFloat("_Slice", currentSlice);
```

**Importante URP**:

* Si ves errores tipo `UNITY_FOG_COORDS`, revisa pragmas y defines URP (el shader incluido aquí ya está ajustado para URP 6000.x).
* Habilita filtrado bilineal/trilineal en el `Texture2DArray` para suavizar transiciones espaciales.

---

## Scripts principales

### `TsunamiTimelineController.cs`

Responsable del **play/pausa/reinicio**, scrubbing con slider y sincronización del `_Slice` del material con el tiempo simulado.

Características:

* **Play/Pause/Restart**
* **Scrub** por slider y por atajos de teclado (opcional)
* **Velocidad** de reproducción (1x, 2x, etc.)
* **Formato de tiempo** (hh:mm:ss) desacoplado del origen (segundos del simulador)

```csharp
using UnityEngine;
using UnityEngine.UI;

public class TsunamiTimelineController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Slider timeline;
    [SerializeField] private Button playBtn;
    [SerializeField] private Button pauseBtn;
    [SerializeField] private Button restartBtn;
    [SerializeField] private TMPro.TextMeshProUGUI timeLabel;

    [Header("Rendering")]
    [SerializeField] private Material waterMaterial;
    [SerializeField] private string sliceProperty = "_Slice";
    [SerializeField, Min(0)] private int firstFrame = 1;   // default 1
    [SerializeField, Min(1)] private int lastFrame = 81;   // default 81
    [SerializeField] private float playbackSpeed = 1f;     // 1x

    [Header("Time Mapping")]
    [Tooltip("Duraciones absolutas por frame en segundos. Si está vacío, se usa paso uniforme.")]
    [SerializeField] private float[] frameTimesSeconds; // opcional: tamaño = (lastFrame - firstFrame + 1)

    private bool isPlaying;
    private int currentFrame;
    private float t;

    private void Awake()
    {
        currentFrame = firstFrame;
        if (timeline)
        {
            timeline.minValue = firstFrame;
            timeline.maxValue = lastFrame;
            timeline.wholeNumbers = true;
            timeline.value = currentFrame;
            timeline.onValueChanged.AddListener(OnScrub);
        }
        if (playBtn) playBtn.onClick.AddListener(Play);
        if (pauseBtn) pauseBtn.onClick.AddListener(Pause);
        if (restartBtn) restartBtn.onClick.AddListener(Restart);
        ApplyFrame(currentFrame);
        UpdateTimeLabel(currentFrame);
    }

    private void Update()
    {
        if (!isPlaying) return;
        float dt = Time.deltaTime * Mathf.Max(0.01f, playbackSpeed);
        AdvanceByTime(dt);
    }

    public void Play() => isPlaying = true;
    public void Pause() => isPlaying = false;

    public void Restart()
    {
        isPlaying = false;
        SetFrame(firstFrame);
    }

    private void OnScrub(float val)
    {
        isPlaying = false;
        SetFrame(Mathf.RoundToInt(val));
    }

    private void AdvanceByTime(float delta)
    {
        // Si no hay mapeo variable de tiempo por frame, avanza 1 frame por segundo * playbackSpeed
        if (frameTimesSeconds == null || frameTimesSeconds.Length == 0)
        {
            t += delta;
            if (t >= 1f)
            {
                t = 0f;
                Step(+1);
            }
            return;
        }

        // Con mapeo: suma tiempo y avanza al siguiente frame cuando se supera su duración
        t += delta;
        int idx = currentFrame - firstFrame; // 0-based
        float dur = Mathf.Max(0.0001f, frameTimesSeconds[Mathf.Clamp(idx, 0, frameTimesSeconds.Length - 1)]);
        if (t >= dur)
        {
            t = 0f;
            Step(+1);
        }
    }

    private void Step(int dir)
    {
        int next = currentFrame + dir;
        if (next > lastFrame) next = lastFrame; // o loop si se desea
        SetFrame(next);
    }

    private void SetFrame(int frame)
    {
        currentFrame = Mathf.Clamp(frame, firstFrame, lastFrame);
        if (timeline) timeline.SetValueWithoutNotify(currentFrame);
        ApplyFrame(currentFrame);
        UpdateTimeLabel(currentFrame);
    }

    private void ApplyFrame(int frame)
    {
        if (waterMaterial)
        {
            waterMaterial.SetFloat(sliceProperty, frame);
        }
    }

    private void UpdateTimeLabel(int frame)
    {
        if (!timeLabel) return;

        // Ejemplo: mapear por índice conocido → tiempo simulado aprox.
        // Puedes reemplazar este switch por un arreglo con tiempos reales del simulador
        string formatted = TimeFormatter.SecondsToClock(GetApproxSecondsForFrame(frame));
        timeLabel.text = formatted;
    }

    // Aproximación: reemplazar con datos reales del simulador
    private int GetApproxSecondsForFrame(int frame)
    {
        // p.ej. frame 1 ≈ 2:03, 80 ≈ 6:00, 81 ≈ 40:30
        if (frame <= firstFrame) return 123;  // 2:03
        if (frame >= lastFrame) return 2430;  // 40:30
        // interpolación lineal simple entre 2:03 y 6:00 para frames intermedios
        float a = 123f;       // s
        float b = 360f;       // 6:00
        float t = (frame - firstFrame) / (float)(lastFrame - firstFrame);
        return Mathf.RoundToInt(Mathf.Lerp(a, b, t));
    }
}
```

### `TimeFormatter.cs`

```csharp
public static class TimeFormatter
{
    public static string SecondsToClock(int totalSeconds)
    {
        if (totalSeconds < 0) totalSeconds = 0;
        int h = totalSeconds / 3600;
        int m = (totalSeconds % 3600) / 60;
        int s = totalSeconds % 60;
        return h > 0 ? $"{h:00}:{m:00}:{s:00}" : $"{m:00}:{s:00}";
    }
}
```

### `HeightArrayLoader.cs` (esqueleto)

Carga texturas sueltas y crea un `Texture2DArray` en tiempo de ejecución o asigna un asset preempaquetado.

```csharp
using UnityEngine;

public class HeightArrayLoader : MonoBehaviour
{
    [SerializeField] private Material waterMaterial;
    [SerializeField] private string arrayProperty = "_HeightArray";
    [SerializeField] private Texture2D[] frames; // asignar en el editor o cargar por ruta

    private void Start()
    {
        if (frames == null || frames.Length == 0) return;
        var w = frames[0].width;
        var h = frames[0].height;
        var array = new Texture2DArray(w, h, frames.Length, TextureFormat.R16, true, false);
        array.wrapMode = TextureWrapMode.Clamp;
        array.filterMode = FilterMode.Bilinear;

        for (int i = 0; i < frames.Length; i++)
        {
            Graphics.CopyTexture(frames[i], 0, 0, array, i, 0);
        }
        array.Apply(true, false);
        waterMaterial.SetTexture(arrayProperty, array);
    }
}
```

---

## Controles de simulación

* **Play/Pausa/Reinicio** desde la UI.
* **Scrubbing** por slider (frames enteros) y atajos de teclado (opcional).
* **Velocidad** de reproducción configurable.
* **_DispScale** (metros) ajustable en el material para dar relieve realista.

> Para suavizar saltos temporales, puede habilitarse una **interpolación** entre frames (mezclando dos slices); ver Roadmap.

---

## Flujo de trabajo

1. Carga la escena principal.
2. Asegúrate de que el `Water_TsunamiArray.mat` tenga asignado `_HeightArray` y un valor razonable de `_DispScale` (p.ej. 10.0 m inicial).
3. Ejecuta en Play Mode, usa el **timeline** para avanzar/pausar y observar la propagación.
4. Ajusta cámara/Cesium y verifica alineación del agua con el terreno.

---

## Rendimiento

* **Mesh de agua**: usa un grid con subdivisión suficiente para evitar aliasing espacial, sin excesos.
* **Filtrado**: habilita bilinear/trilineal en el array para suavizar.
* **Batching**: un solo mesh/material si es posible.
* **WebGL**: evalúa resoluciones menores de heightmap o streaming progresivo.

---

## Solución de problemas

* **“Se ve plano”**: aumenta `_DispScale` (m), confirma que el `Texture2DArray` es **R16** y que **no** usa sRGB.
* **Cuadro negro/quad inesperado**: suele ser un **mesh placeholder** o un objeto de debug con material faltante. Busca en la jerarquía "Quad/Plane/Debug" y revisa el **Canvas** o *Gizmos*.
* **Shader error (URP)**: si aparece `UNITY_FOG_COORDS`, asegúrate de usar la versión incluida del shader y que el proyecto está en URP.
* **Desfase tiempo–frame**: ajusta `frameTimesSeconds` o `GetApproxSecondsForFrame()` con los tiempos reales del simulador.

---

## Roadmap Unity

* Interpolación temporal entre frames (blend de slices).
* Espuma basada en **gradiente espacial** (∇height) y **fresnel**/subsuperficie.
* UI mejorada (velocidades predefinidas, saltos a hitos de tiempo).
* Streaming/LOD de heightmaps y tiles urbanos.
* Export a **WebGL**.

---

## Backend (referencia rápida)

El backend **FastAPI** provee los frames/metadata (y usa **MinIO** para almacenamiento). Este cliente puede apuntar a un **loader** que descargue frames y construya el `Texture2DArray`. Para detalles, ver el README del repo de backend.

---


