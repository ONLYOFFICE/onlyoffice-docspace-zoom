import { useEffect, useState } from "react";
import getState from "../services/stateHandler";
import "./AdditionalInfoBox.css";
import i18n from "../i18n";
import FileInfo from "./FileInfo";

interface AdditionalInfoBoxProps {
    fileInfo: any;
    showPermissions: boolean;
}

function AdditionalInfoBox(props: AdditionalInfoBoxProps) {
    const appState = getState();

    const [selectedTitle, setSelectedTitle] = useState(i18n.t("New Document"));
    const [selectedFormat, setSelectedFormat] = useState("docx");
    const [selectedPermission, setselectedPermission] = useState(1);

    useEffect(() => {
        appState.meetingState.additionalInfo.title = selectedTitle;
        appState.meetingState.additionalInfo.format = selectedFormat;
        appState.meetingState.additionalInfo.permission = selectedPermission;
    }, [appState, selectedTitle, selectedFormat, selectedPermission])

    return (
        <div className="additionalInfoBox backgroundBox">
            {props.fileInfo ? (<></>) : (
                <div className="fileCreateInfo">
                    <div className="inputHolder">
                        <div className="label">{i18n.t("Title")}</div>
                        <input type="text" name="document-title" value={i18n.t("New Document")} onChange={(ev) => setSelectedTitle(ev.target.value)} />
                    </div>
                    <div className="formats">
                        <label className="fileFormatLabel">
                            <input className="selectable-label" type="radio" hidden name="document-format" value="docx" onChange={() => setSelectedFormat("docx")} checked />
                            <img src="/images/icons/64/docx.svg" alt="docx" />
                            <span>{i18n.t("Document")}</span>
                        </label>
                        <label className="fileFormatLabel">
                            <input className="selectable-label" type="radio" hidden name="document-format" value="xlsx" onChange={() => setSelectedFormat("xlsx")} />
                            <img src="/images/icons/64/xlsx.svg" alt="xlsx" />
                            <span>{i18n.t("Spreadsheet")}</span>
                        </label>
                        <label className="fileFormatLabel">
                            <input className="selectable-label" type="radio" hidden name="document-format" value="pptx" onChange={() => setSelectedFormat("pptx")} />
                            <img src="/images/icons/64/pptx.svg" alt="pptx" />
                            <span>{i18n.t("Presentation")}</span>
                        </label>
                    </div>
                </div>
            )}
            <FileInfo fileInfo={props.fileInfo} />
            {props.showPermissions ? (
                <div className="permissionForm">
                    <span className="fw600">{i18n.t("Please set access rights of Zoom participants:")}</span>
                    <div className="optionBox">
                        <label>
                            <input type="radio" name="permission" value="1" onChange={() => setselectedPermission(1)} checked />
                            <div>
                                <span className="fw600">{i18n.t("Edit")}</span>
                                <span className="help">{i18n.t("All participants will edit the document with you")}</span>
                            </div>
                        </label>
                    </div>
                    <div className="optionBox">
                        <label>
                            <input type="radio" name="permission" value="0" onChange={() => setselectedPermission(0)} />
                            <div>
                                <span className="fw600">{i18n.t("LiveView")}</span>
                                <span className="help">{i18n.t("All participants will see your screen")}</span>
                            </div>
                        </label>
                    </div>
                </div>
            ) : (<></>)}
            <div className="buttons">
                <div className="button" id="additionalSubmit">{i18n.t("Open")}</div>
                <div className="button secondary" id="additionalCancel">{i18n.t("Cancel")}</div>
            </div>
        </div>
    );
}

export { AdditionalInfoBox, type AdditionalInfoBoxProps };