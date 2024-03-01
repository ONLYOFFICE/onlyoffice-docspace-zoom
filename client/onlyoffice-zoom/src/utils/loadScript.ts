const loading: { [id: string]: Promise<unknown>; } = {};

const loadScript = async (url: string, id: string) => {
  if (document.getElementById(id)) {
    if (window.DocSpace) {
      return new Promise((resolve) => resolve);
    } else {
      return loading[id];
    }
  }

  const promise = new Promise((resolve, reject) => {
    try {
      const script = document.createElement("script");
      script.setAttribute("type", "text/javascript");
      script.setAttribute("id", id);

      script.onload = resolve;
      script.onerror = reject;

      script.src = url;
      script.async = true;

      document.body.appendChild(script);
    } catch (e) {
      reject(e);
    }
  });
  loading[id] = promise;
  return promise;
};

export default loadScript;