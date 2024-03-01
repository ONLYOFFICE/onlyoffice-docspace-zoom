import { useCallback, useEffect, useState } from "react";
import zoomSdk from "@zoom/appssdk";
import DocSpaceFrame from "./DocSpaceFrame";
import getState from "../services/stateHandler";
import * as signalR from "../services/signalRManager";
import { Trans } from 'react-i18next';
import FilePicker from "./FilePicker";
import CollaborationChangePayload from "../models/CollaborationChangePayload";
import { AdditionalInfoBox, AdditionalInfoBoxProps } from "./AdditionalInfoBox";

interface FileHandlerProps {
    promptJoinCollaborate: () => Promise<void>;
    getCollaborationChangePayload: () => CollaborationChangePayload;
}

function FileHandler({ promptJoinCollaborate, getCollaborationChangePayload }: FileHandlerProps) {
    const [infoProps, setInfoProps] = useState<AdditionalInfoBoxProps | null>(null);
    const appState = getState();

    const OnFileSelected = useCallback(async (event: any) => {
        console.log("OnFileSelected", event)
        appState.meetingState.currentFileId = event ? event.id : -1;
        if (appState.meetingState.isHost && !appState.meetingState.isFirstPick) {
            if (appState.meetingState.currentFileId === -1) {
                setInfoProps({ fileInfo: event, showPermissions: false });
            } else {
                await signalR.invoke("CollaborateChange", getCollaborationChangePayload());
            }
        } else {
            appState.meetingState.isFirstPick = false;
            setInfoProps({ fileInfo: event, showPermissions: true });
        }
    }, [appState, setInfoProps, getCollaborationChangePayload]);

    if (infoProps) {
        return (
            <AdditionalInfoBox fileInfo={infoProps.fileInfo} showPermissions={infoProps.showPermissions} />
        );
    } else {
        return (
            <FilePicker promptJoinCollaborate={promptJoinCollaborate} onFileSelected={OnFileSelected} />
        );
    }
}

export default FileHandler;