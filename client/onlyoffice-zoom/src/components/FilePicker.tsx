import { useCallback, useEffect, useRef, useState } from "react";
import zoomSdk from "@zoom/appssdk";
import DocSpaceFrame from "./DocSpaceFrame";
import getState from "../services/stateHandler";
import * as signalR from "../services/signalRManager";
import { Trans } from 'react-i18next';
import "./FilePicker.css";

interface FilePickerProps {
    promptJoinCollaborate: () => Promise<void>;
    onFileSelected: (fileInfo: any) => Promise<void>;
}

function FilePicker({ promptJoinCollaborate, onFileSelected }: FilePickerProps) {
    const appState = getState();
    const [checkCollaborationTimeout, setCheckCollaborationTimeout] = useState<NodeJS.Timeout | null>(null);

    const [isInit, setIsInit] = useState(false);
    const [promptJoin, setPromptJoin] = useState(true);
    const [pickerConfig, setPickerConfig] = useState<any>(null);
    const uploadInput = useRef<HTMLInputElement>(null);

    const refreshState = useCallback(async () => {
        if (!appState.payload?.homeTenant) return;

        try {
            const appContext = await zoomSdk.getAppContext();
            let response = await fetch(`/zoomservice/zoom/state?accountId=${appState.payload.homeTenant}&noRedirect=true`, {
                headers: {
                    "x-zoom-app-context": appContext.context
                }
            });

            if (!response.ok) throw new Error("Unknown response from state handler");

            window.location.assign(await response.text());
        } catch (e) {
            console.log("Error while refreshing state", e);
        }
    }, [appState]);

    useEffect(() => {
        async function onInitPicker() {
            const result = await signalR.invoke("CheckCollaboration");
            if (result) {
                setCheckCollaborationTimeout(setTimeout(onInitPicker, 1000));
                if (promptJoin) {
                    setPromptJoin(false);
                    await promptJoinCollaborate();
                }
                return;
            }

            if (appState.payload?.homeTenant) {
                await refreshState();
                return;
            }

            const isUser = await signalR.invoke("CheckIfUser");
            if (isUser) {
                // ToDo: showMessage("Sorry, you cannot create new rooms and files. Please wait until Owner or Power User start the collaboration session.");
                return;
            }

            setPickerConfig({
                mode: "file-selector",
                selectorType: "userFolderOnly",
                editorType: "desktop",
                showHeader: false,
                showTitle: true,
                showMenu: false,
                showFilter: false,
                events: {
                    onSelectCallback: onFileSelected
                }
            });
        }

        if (!isInit) {
            setIsInit(true);
            onInitPicker();
        }

        return () => {
            if (checkCollaborationTimeout) {
                clearTimeout(checkCollaborationTimeout);
                setCheckCollaborationTimeout(null);
            }
        }
    }, [isInit, appState, checkCollaborationTimeout, promptJoin,
        setCheckCollaborationTimeout, setIsInit, setPromptJoin, promptJoinCollaborate, refreshState, onFileSelected]);

    const onFileCreateClick = useCallback(() => {
        onFileSelected(null);
    } ,[onFileSelected]);

    const onFileUploadClick = useCallback(() => {
        if (uploadInput.current) {
            uploadInput.current.click();
        }
    } ,[uploadInput]);

    const onFileUpload = useCallback(async () => {
        if (!uploadInput.current || !uploadInput.current.files || uploadInput.current.files!.length !== 1) return;

        const data = new FormData();
        data.append("file", uploadInput.current.files[0]);

        const appContext = await zoomSdk.getAppContext();
        try {
            const response = await fetch(`/zoomservice/zoom/upload`, {
                method: "POST",
                body: data,
                headers: {
                    "x-zoom-app-context": appContext.context
                }
            });
            const json = await response.json();
            if (json.error) {
                if (json.error === "quota") {
                    //await onQuotaHit();
                    return;
                } else {
                    throw new Error(json.error);
                }
            }
            console.log("Uploaded file", json);
            onFileSelected(json);
        } catch (ex) {
            console.error("Couldn't upload a file", ex);
        }
    }, [onFileSelected]);

    if (pickerConfig) {
        return (
            <div className="picker backgroundBox">
                <div className="helpMessage">
                    <Trans><b>Choose, create or upload file</b> to collaborate with other Zoom meeting participants.<br/>New files will be saved in new DocSpace room.</Trans>
                </div>
                <div className="filePickerHeaderBox">
                    <span className="filePickerHeader">ONLYOFFICE</span>
                    <div className="filePickerButtons">
                        <input ref={uploadInput} onChange={onFileUpload} hidden type="file" name="file" accept=".docx,.xlsx,.pptx" />
                        <div onClick={onFileUploadClick}>
                            <svg viewBox="0 0 18 17" fill="none" xmlns="http://www.w3.org/2000/svg">
                                <path fill-rule="evenodd" clip-rule="evenodd" d="M9.00002 1.0625C9.31019 1.0625 9.60487 1.19804 9.80673 1.43353L16.1817 8.87103C16.4518 9.18608 16.5137 9.6295 16.3403 10.0065C16.1669 10.3834 15.79 10.625 15.375 10.625H12.1811V12.75H10.0561V9.5625C10.0561 8.9757 10.5318 8.5 11.1186 8.5H13.0649L9.00002 3.75763L4.93512 8.5H6.86847C7.45528 8.5 7.93097 8.9757 7.93097 9.5625V12.75H5.80597V10.625H2.62502C2.21008 10.625 1.8331 10.3834 1.65972 10.0065C1.48634 9.6295 1.54827 9.18608 1.81831 8.87103L8.19331 1.43353C8.39516 1.19804 8.68985 1.0625 9.00002 1.0625ZM16.4375 13.8125V15.9375H1.56252V13.8125H16.4375Z" fill="#A3A9AE" />
                            </svg>
                        </div>
                        <div className="primary" onClick={onFileCreateClick}>
                            <svg viewBox="0 0 18 17" fill="none" xmlns="http://www.w3.org/2000/svg">
                                <path fill-rule="evenodd" clip-rule="evenodd" d="M9 1C8.17157 1 7.5 1.67157 7.5 2.5V7H3C2.17157 7 1.5 7.67157 1.5 8.5C1.5 9.32843 2.17157 10 3 10H7.5V14.5C7.5 15.3284 8.17157 16 9 16C9.82843 16 10.5 15.3284 10.5 14.5V10H15C15.8284 10 16.5 9.32843 16.5 8.5C16.5 7.67157 15.8284 7 15 7H10.5V2.5C10.5 1.67157 9.82843 1 9 1Z" fill="white" />
                            </svg>
                        </div>
                    </div>
                </div>
                <div className="pickerHolder">
                    <DocSpaceFrame id="zoom-ds-picker-frame" config={pickerConfig} />
                </div>
            </div>
        );
    } else {
        return (
            <div className="picker"></div>
        );
    }
}

export default FilePicker;