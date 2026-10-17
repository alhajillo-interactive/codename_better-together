
using UnityEngine;

public class ControladorSol : MonoBehaviour
{
    [Header("Configuracion del sol")]
    public Light sun;

    [Header("Skybox")]
    public Material skyboxMaterial;

    [Header("Duracion del dia")]
    public float duracionDia = 1f;

    [Header("Orientacion")]
    public float rotacionHorizontal = 170f;

    private float temporizador = 0f;

    void Start()
    {
        // Si no se asigna un material manualmente,
        // utiliza el Skybox de la escena.
        if (skyboxMaterial == null)
        {
            skyboxMaterial = RenderSettings.skybox;
        }
    }

    void Update()
    {
        if (sun == null || duracionDia <= 0f)
        {
            return;
        }

        temporizador += Time.deltaTime;

        float progreso = Mathf.Clamp01(temporizador / duracionDia);
        float angulo = Mathf.Lerp(0f, 180f, progreso);

        sun.transform.rotation = Quaternion.Euler(
            angulo,
            rotacionHorizontal,
            0f
        );

        // Sincronizar la direccion del sol del Skybox
        if (skyboxMaterial != null &&
            skyboxMaterial.HasProperty("_sun_dir"))
        {
            Vector3 direccionSol = sun.transform.forward;

            skyboxMaterial.SetVector(
                "_sun_dir",
                new Vector4(
                    direccionSol.x,
                    direccionSol.y,
                    direccionSol.z,
                    0f
                )
            );
        }
    }
}
