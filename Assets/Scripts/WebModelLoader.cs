using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Threading.Tasks;   // Para Task<bool>
using GLTFast;                 // Para GltfImport

public class WebModelLoader : MonoBehaviour
{
    // ---------------------------------------------------------------
    //  Clases de datos para JSON
    // ---------------------------------------------------------------
    [System.Serializable]
    public class MessageData
    {
        public string type;
        public int id;
    }

    [System.Serializable]
    public class ModelInfo
    {
        public int id;
        public string titulo;
        public string tipo;
        public string url;
    }

    // ---------------------------------------------------------------
    //  Inicio
    // ---------------------------------------------------------------
    void Start()
    {
        Debug.Log("✅ WebGL listo. Esperando mensajes desde React...");
    }

    // ---------------------------------------------------------------
    //  Llamado desde JavaScript (index.html)
    //  JS: unityInstance.SendMessage("WebBridge", "OnMessageReceived", json);
    // ---------------------------------------------------------------
    public void OnMessageReceived(string message)
    {
        Debug.Log($"📩 Mensaje recibido desde React (raw): {message}");

        try
        {
            // Limpieza básica por si viene con comillas extras
            message = message.Trim();
            if (message.StartsWith("\"") && message.EndsWith("\""))
                message = message.Substring(1, message.Length - 2);

            message = message.Replace("\\\"", "\"").Replace("'", "\"");

            Debug.Log($"🧹 Mensaje limpio final: {message}");

            var data = JsonUtility.FromJson<MessageData>(message);
            if (data == null)
            {
                Debug.LogError("❌ JsonUtility devolvió null. Revisa el formato del JSON.");
                return;
            }

            Debug.Log($"✅ ID recibido desde Laravel: {data.id}");

            if (data.type == "LOAD_MODEL")
            {
                // Pedir al backend la info del modelo (URL, tipo, etc.)
                StartCoroutine(RequestModelInfo(data.id.ToString()));
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"❌ Error al procesar mensaje: {ex.Message}\nPayload: {message}");
        }
    }

    // ---------------------------------------------------------------
    //  Llama al backend Laravel para obtener la URL del modelo
    //  GET http://127.0.0.1:8000/planos/3d/{id}/modelo
    // ---------------------------------------------------------------
    IEnumerator RequestModelInfo(string id)
    {
        string apiUrl = $"http://127.0.0.1:8000/planos/3d/{id}/modelo";
        Debug.Log($"🌐 Solicitando metadatos a: {apiUrl}");

        UnityWebRequest req = UnityWebRequest.Get(apiUrl);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"❌ Error en la solicitud: {req.error}");
            yield break;
        }

        string response = req.downloadHandler.text;
        Debug.Log($"📜 Respuesta completa del servidor: {response}");

        ModelInfo data = null;
        try
        {
            data = JsonUtility.FromJson<ModelInfo>(response);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"❌ Error al parsear JSON del servidor: {ex.Message}");
            yield break;
        }

        if (data == null || string.IsNullOrEmpty(data.url))
        {
            Debug.LogError("❌ Respuesta del servidor inválida o sin URL de modelo.");
            yield break;
        }

        Debug.Log($"📦 Cargando modelo desde: {data.url}");

        // Por ahora asumimos que es GLB (tu Laravel ya devuelve tipo GLB)
        yield return StartCoroutine(LoadGLB(data.url));
    }

    // ---------------------------------------------------------------
    //  Descargar y cargar GLB con GLTFast usando corrutina
    // ---------------------------------------------------------------
   IEnumerator LoadGLB(string url)
    {
        Debug.Log("⬇️ Descargando GLB desde: " + url);

        // Descargar todo a buffer
        UnityWebRequest req = UnityWebRequest.Get(url);
        req.downloadHandler = new DownloadHandlerBuffer();
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("❌ Error bajando GLB: " + req.error);
            yield break;
        }

        byte[] glbBytes = req.downloadHandler.data;
        Debug.Log("📥 Importando GLB con GLTFast...");

        // 1. Limpiar modelos anteriores del objeto padre (WebModelLoader)
        // Esto previene tener que buscar el 'ModeloGLB' anterior.
        foreach (Transform child in this.transform)
        {
            Destroy(child.gameObject);
        }

        GltfImport import = new GltfImport();

        // 2. Lanzar la tarea asíncrona de carga (usa la sobrecarga que toma bytes)
        // Nota: Mantenemos LoadGltfBinary ya que ya tienes los bytes descargados
        Task<bool> loadTask = import.LoadGltfBinary(glbBytes, new System.Uri(url));

        // 3. Esperar a que la Task termine
        yield return new WaitUntil(() => loadTask.IsCompleted);

        if (loadTask.IsFaulted)
        {
            Debug.LogError("❌ GLTFast falló en la tarea de carga: " + loadTask.Exception);
            yield break;
        }

        bool ok = loadTask.Result;
        if (!ok)
        {
            Debug.LogError("❌ GLTFast no pudo importar el GLB.");
            yield break;
        }

        Debug.Log("✅ GLB importado, instanciando modelo con Instantiator...");

        // 4. Crear el objeto raíz del modelo
        GameObject root = new GameObject("ModeloGLB");
        root.transform.SetParent(this.transform, false); // Asignarlo como hijo de este script

        // 5. Instanciar usando GameObjectInstantiator (El método más robusto para WebGL)
        var instantiator = new GameObjectInstantiator(import, root.transform);

        // Usar InstantiateMainScene sincrónico (la instanciación de objetos ya es rápida)
        import.InstantiateMainScene(instantiator);
        
        // ** Opcional: Centrar el modelo si está muy lejos del origen **
        // CenteringUtils.CenterModel(root); 

        Debug.Log("🎉 Modelo GLB cargado con éxito.");
    }
}
