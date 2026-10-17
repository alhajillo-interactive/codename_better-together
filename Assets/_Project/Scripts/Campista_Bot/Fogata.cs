using UnityEngine;

public class Fogata : MonoBehaviour
{
    [Header("Estado de la Fogata")]
    public int lenasActuales = 0;
    public int lenasNecesarias = 5;
    public bool tieneFosforo = false;
    public bool fogataEncendida = false;

    // Funcion para saber si se prenderá ya el fuego
    void Update()
    {
        if (lenasActuales >= lenasNecesarias && tieneFosforo && !fogataEncendida)
        {
            Debug.Log("¡La fogata tiene todo listo para encenderse!");
            fogataEncendida = true;
 
        }
    }
}