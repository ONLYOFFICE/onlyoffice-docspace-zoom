
import "./FileInfo.css";

function FileInfo({ fileInfo }: any) {
    if (fileInfo && fileInfo.id) {
        return (
            <div className="fileInfo">
                <div>
                    <img src={fileInfo.icon ? fileInfo.icon : "/images/icons/64/file.svg"} alt="icon" />
                    <span>{fileInfo.title}</span>
                </div>
            </div>
        );
    } else {
        return (
            <div className="fileInfo"></div>
        );
    }
}

export default FileInfo;