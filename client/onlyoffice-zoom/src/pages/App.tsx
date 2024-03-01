import { useEffect, useState, useCallback } from "react";
import { useNavigate } from "react-router-dom";
import zoomSdk, { OnAuthorizedEvent } from "@zoom/appssdk";
import getState from "../services/stateHandler";
import Payload from "../models/Payload";
import loadScript from "../utils/loadScript";
import toLowerCaseProps from "../utils/jsonUtils";
import Confirm from "../components/Confirm";

declare global {
  interface Window {
    DocSpace: any;
  }
}

function App() {
  const navigate = useNavigate();
  const appState = getState();
  const [docSpaceReady, setDocSpaceReady] = useState(false);
  const [zoomReady, setZoomReady] = useState(false);
  const [authReady, setAuthReady] = useState(false);
  const [renderConfirm, setRenderConfirm] = useState(false);

  useEffect(() => {
    async function initDocSpace() {
      const payloadParam = new URLSearchParams(window.location.search).get("payload");
      if (payloadParam == null) {
        console.log("Missing payload from ZoomService, falling back to Landing Page");
        navigate("/landing");
      } else {
        const payload = toLowerCaseProps<Payload>(JSON.parse(payloadParam));
        console.debug("ZoomService Payload", payload);
        appState.payload = payload;
        await loadScript(`${payload.docSpaceUrl}/static/scripts/api.js`, "ds");
        setDocSpaceReady(true);
      }
    }
    initDocSpace();
  }, [appState, navigate, setDocSpaceReady]);

  const onAuthCallback = useCallback(async (success: boolean) => {
    setRenderConfirm(false);
    if (success) {
      setAuthReady(true);
    } else {
      // ToDo: Handle login error
    }
  }, [setRenderConfirm, setAuthReady]);

  const onZoomAuth = useCallback(async (event: OnAuthorizedEvent) => {
    console.debug("Zoom JS SDK onAuthorized", event);

    if (!event.result) {
      console.debug(`User was not authorized. Reason: ${event.code}`);
      await onAuthCallback(false);
      return;
    }

    const homeResponse = await fetch(event.redirectUri, {
      method: "POST",
      headers: {
        "Content-Type": "application/json"
      },
      body: JSON.stringify({
        code: event.code,
        state: event.state,
        challenge: appState.payload?.challenge,
        redirectUri: event.redirectUri
      })
    });

    console.debug("Api System Payload Home Response", homeResponse);

    try {
      const link = await homeResponse.text();
      window.location.assign(link);
    } catch {
      await onAuthCallback(false);
    }
  }, [appState, onAuthCallback]);

  useEffect(() => {
    async function configureZoomSdk() {
      try {
        const configResponse = await zoomSdk.config({
          size: { width: 480, height: 360 },
          capabilities: [
            'getRunningContext',
            'getUserContext',
            'getAppContext',
            'getMeetingUUID',

            'openUrl',

            'authorize',
            'promptAuthorize',
            'onAuthorized',

            'startCollaborate',
            'joinCollaborate',
            'onCollaborateChange'
          ],
        });
        console.debug("ZoomSdk Init", configResponse);

        zoomSdk.onAuthorized(onZoomAuth);

        appState.zoomConfig = configResponse;
        setZoomReady(true);
      } catch (e) {
        console.log("Error while configuring ZoomSDK", e);
        setZoomReady(true);
      }
    }
    configureZoomSdk();
  }, [appState, setZoomReady, onZoomAuth]);

  useEffect(() => {
    async function onReady() {
      if (docSpaceReady) {
        console.debug("DocSpace is ready");
      }
      if (zoomReady) {
        console.debug("Zoom is ready");
      }

      if (docSpaceReady && zoomReady) {
        console.debug("All ready, processing auth");

        if (appState.payload?.confirmLink) {
          console.log("ConfirmLink is not null, proccessing");
          setRenderConfirm(true);
        } else {
          if (appState.zoomConfig?.browserVersion.startsWith("applewebkit")) {
            await zoomSdk.openUrl({
              url: `${window.location.origin}/zoomservice/zoom/install?state=${appState.payload?.state}`
            });
            // ToDo: message
            // showMessage("Please authorize using browser.");
          } else {
            // check if we are authorized in zoom or not
            // authorizePrompt if we are guest
            // else authorize
            await zoomSdk.authorize({
              state: appState.payload?.state as string,
              codeChallenge: appState.payload?.challenge as string
            });
          }
        }
      }
    }
    onReady();
  }, [appState, docSpaceReady, zoomReady, setRenderConfirm, navigate]);

  useEffect(() => {
    async function onAuthReady() {
      if (authReady) {
        const runningContext = await zoomSdk.getRunningContext();
        if (runningContext.context === "inMeeting" || runningContext.context === "inCollaborate") {
          navigate("/meeting", { state: { runningContext: runningContext.context }});
        } else {
          navigate("/manager", { state: { id: null } });
        }
      }
    }
    onAuthReady();
  }, [authReady, navigate]);

  let confirmContent = null;
  if (renderConfirm) {
    confirmContent = (
      <Confirm url={appState.payload?.confirmLink as string} onAuthCallback={onAuthCallback} />
    );
  }

  return (
    <div className="app">{confirmContent}</div>
  );
}

export default App;
