import { useLocation } from "react-router-dom";
import zoomSdk, { OnCollaborateChangeEvent } from "@zoom/appssdk";
import DocSpaceFrame from "../components/DocSpaceFrame";
import { useCallback, useEffect, useState } from "react";
import getState from "../services/stateHandler";
import * as signalR from "../services/signalRManager";
import CollaborationPayload from "../models/CollaborationPayload";
import FileHandler from "../components/FileHandler";
import CollaborationChangePayload from "../models/CollaborationChangePayload";

function Manager() {
    const location = useLocation();
    const appState = getState();

    const [collaborationTimeout, setCollaborationTimeout] = useState<NodeJS.Timeout | null>(null);
    const [isInit, setIsInit] = useState(false);
    const [renderPicker, setRenderPicker] = useState(false);

    const getCollaboration = useCallback(async () => {
        await signalR.invoke("GetCollaboration");
    }, []);

    const promptJoinCollaborate = useCallback(async () => {
        // double check, we might already be in a collaboration
        const runningContext = await zoomSdk.getRunningContext();
        if (runningContext.context === "inCollaborate") {
            await getCollaboration();
        } else if (!appState.meetingState.isHost) {
            try {
                await zoomSdk.joinCollaborate();
            } catch (ex: any) {
                if (ex.code === "10085") {
                    // there is no collaboration to join to
                    // ToDo: init file picker      destroyLoginFrameAndInitFilePicker();
                    return;
                }
            }
            // ToDo: show message
            // showMessage("Meeting participant started editing session", { button: {
            //     text: "Join",
            //     action: async () => {
            //         await zoomSdk.joinCollaborate();
            //     }
            // }});
        }
    }, [appState, getCollaboration]);

    const openByCollaborationPayload = useCallback(async (collaboration: CollaborationPayload) => {
        let handled = false;
        try {
            if (collaborationTimeout != null) {
                clearTimeout(collaborationTimeout);
                setCollaborationTimeout(null);
            }
            switch (collaboration.status) {
                case 0: // pending
                    if (!appState.meetingState.isFirstPick) {
                        // ToDo: showMessage("Collaboration host is changing a document, please wait");
                        setCollaborationTimeout(setTimeout(getCollaboration, 1000));
                    }
                    handled = true;
                    break;
                case 1: // inRoom
                    if (collaboration.roomId) {
                        // ToDo: destroyLoginFrameAndInitManager(collaboration.roomId, true);
                    }
                    handled = true;
                    break;
                case 2: // inFile
                    if (collaboration.fileId) {
                        if (appState.meetingState.isHost) {
                            // ToDo: destroyLoginFrameAndInitEditor(collaboration.fileId, true, onGoBack);
                        } else {
                            // ToDo: destroyLoginFrameAndInitEditor(collaboration.fileId, true);
                        }
                    }
                    handled = true;
                    break;
            }
        } catch {
            handled = false;
        }

        if (handled) return;

        console.error("Failed to process collaboration object", collaboration);
    }, [appState, collaborationTimeout, setCollaborationTimeout, getCollaboration]);

    const handleCollaborationPayload = useCallback(async (collaboration: CollaborationPayload) => {
        if (appState.meetingState.isHost) {
            openByCollaborationPayload(collaboration);
        } else {
            const runningContext = await zoomSdk.getRunningContext();
            if (runningContext.context === "inMeeting") {
                await promptJoinCollaborate();
            } else if (runningContext.context === "inCollaborate") {
                openByCollaborationPayload(collaboration);
            }
        }
    }, [appState, openByCollaborationPayload, promptJoinCollaborate]);

    const onCollaboration = useCallback(async (collaboration: CollaborationPayload) => {
        console.log("Collaboration changed", collaboration);
        appState.payload!.collaboration = collaboration;
        handleCollaborationPayload(collaboration);
    }, [appState, handleCollaborationPayload]);

    const onCollaborationStarting = useCallback(async () => {
        console.log("Someone pressed collaborate");
        await promptJoinCollaborate();
    }, [promptJoinCollaborate]);

    async function onCollaborationChanging() {
        // showMessage("Collaboration host is changing a document, please wait");
        // destroyFrame();
    }

    async function onQuotaHit() {
        // showMessage("The quota of your DocSpace is almost reached.");
    }

    const getCollaborationChangePayload = useCallback(() => {
        const title = appState.meetingState.additionalInfo.title + "." + appState.meetingState.additionalInfo.format;

        const payload = new CollaborationChangePayload();
        payload.fileId = appState.meetingState.currentFileId ? appState.meetingState.currentFileId.toString() : null;
        payload.collaborationType = appState.meetingState.additionalInfo.permission;
        payload.title = appState.meetingState.currentFileId && appState.meetingState.currentFileId === -1 ? title : null;

        return payload
    }, [appState]);

    const onZoomCollaborateChange = useCallback(async (event: OnCollaborateChangeEvent) => {
        try {
            switch (event.action) {
                case "start":
                    // ToDo: clearTimeout(checkCollaborationTimeout);
                    appState.meetingState.isHost = true;
                    appState.meetingState.currentCollaborationId = event.collaborateUUID as string;
                    await signalR.invoke("CollaborateStart", event.collaborateUUID, getCollaborationChangePayload());
                    break;

                case "end":
                    appState.meetingState.isFirstPick = true;
                    appState.meetingState.isHost = false;
                    appState.meetingState.currentFileId = null;
                    // ToDo: destroyLoginFrameAndInitFilePicker();
                    await signalR.invoke("CollaborateEnd");
                    appState.meetingState.currentCollaborationId = null;
                    break;

                case "join":
                    // ToDo: clearTimeout(checkCollaborationTimeout);
                    appState.meetingState.isHost = false;
                    appState.meetingState.currentCollaborationId = null;
                    break;

                case "leave":
                    appState.meetingState.isFirstPick = true;
                    appState.meetingState.currentFileId = null;
                    // ToDo: destroyLoginFrameAndInitFilePicker(true);
                    break;
            }
        } catch (e) {
            console.log("SignalR error", e);
        }
    }, [appState, getCollaborationChangePayload]);

    const onGoBack = useCallback(async () => {
        await signalR.invoke("CollaborateChanging");
        // ToDo: await destroyLoginFrameAndInitFilePicker();
    }, []);

    useEffect(() => {
        async function onInit() {
            console.log("Initing Meeting Page");
            const meetingUUID = await zoomSdk.getMeetingUUID();
            appState.meetingState.currentMeetingId = meetingUUID.meetingUUID;
            await signalR.initSignalR(appState.meetingState.currentMeetingId);

            zoomSdk.onCollaborateChange(onZoomCollaborateChange);

            if (appState.payload?.collaboration) {
                if (new URLSearchParams(window.location.search).get("outdated") === "true") {
                    console.log("Collaboration payload is old, requesting new", appState.payload?.collaboration);
                    await getCollaboration();
                } else {
                    console.log("Collaboration payload recieved", appState.payload?.collaboration);
                    window.history.pushState(null, "", location.toString() + "&outdated=true");
                    handleCollaborationPayload(appState.payload?.collaboration);
                }
                return;
            }

            setRenderPicker(true);
        }

        if (!isInit) {
            console.log("Init Meeting Page");
            setIsInit(true);
            onInit();
        }

    }, [location, appState, isInit,
        getCollaboration, onCollaborationStarting, onCollaborationChanging, onQuotaHit,
        handleCollaborationPayload, onCollaboration, onZoomCollaborateChange, setRenderPicker, setIsInit]);


    if (renderPicker) {
        console.log("Rendering picker");
        return (
            <div className="frameHolder">
                <FileHandler promptJoinCollaborate={promptJoinCollaborate} getCollaborationChangePayload={getCollaborationChangePayload} />
            </div>
        );
    } else {
        return (
            <div className="frameHolder"></div>
        );
    }
}

export default Manager;
