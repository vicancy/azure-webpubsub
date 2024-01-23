import React, { useEffect, useState } from "react";
import { useSearchParams } from "react-router-dom";
import moment from "moment";
import { ReadonlyTabs } from "../components/Tabs";
import { ResizablePanel } from "../components/ResizablePanel";
import { useDataContext } from "../providers/DataContext";
import { HttpHistoryItem } from "../models";
import { Button } from "@fluentui/react-components";
import { bundleIcon, Delete24Filled, Delete24Regular, ArrowDownload24Filled, ArrowDownload24Regular } from "@fluentui/react-icons";

import { Dialog, DialogTrigger, DialogSurface, DialogTitle, DialogBody, DialogActions, DialogContent } from "@fluentui/react-components";

const ClearHistoryIcon = bundleIcon(Delete24Filled, Delete24Regular);
const ExportHistoryIcon = bundleIcon(ArrowDownload24Filled, ArrowDownload24Regular);

export interface RequestHistoryProps {
  onUnreadChange: (unread: number) => void;
}

function convertToHAR(items: HttpHistoryItem[]): HarFile {
  return {
    log: {
      version: "1.2",
      creator: {
        name: "awps-tunnel",
        version: "1.0", // to do: uniform the package.json version with server
      },
      entries: items.map((entry) => ({
        startedDateTime: new Date(entry.requestAtOffset).toISOString(),
        time: entry.responseAtOffset ? -1 : entry.responseAtOffset! - entry.requestAtOffset,
        request: {
          method: entry.methodName,
          url: entry.url,
          httpVersion: "HTTP/1.1",
          headers: [], // todo: extract headers and body from http raw
          queryString: [],
          cookies: [],
          headersSize: -1,
          bodySize: 0,
        },
        response: {
          status: entry.code ?? 0,
          statusText: "",
          httpVersion: "HTTP/1.1",
          headers: [],
          content: {
            size: entry.responseRaw?.length ?? 0,
            mimeType: "",
            text: ""
          },
          headersSize: -1,
          bodySize: 0,
        },
        timings: {
          blocked: -1,
          dns: -1,
          ssl: -1,
          connect: -1,
          send: -1,
          wait: -1,
          receive: -1,
          _blocked_queueing: -1,
        },
      })),
    },
  };
}

export function RequestHistory(props: RequestHistoryProps) {
  const [items, setItems] = useState<HttpHistoryItem[]>([]);
  const [openDialog, setOpenDialog] = useState<boolean>(false);
  const [selectedItem, setSelectedItem] = useState<HttpHistoryItem | undefined>(undefined);
  const [searchParams] = useSearchParams();
  const detailId = parseInt(searchParams.get("detailId") ?? "-1");
  const { data, dataFetcher } = useDataContext();

  useEffect(() => {
    setItems(data.trafficHistory);
    props.onUnreadChange(data.trafficHistory.filter((s) => s.unread).length);
  }, [props, data.trafficHistory]);
  useEffect(() => {
    if (selectedItem) {
      return;
    }
    if (detailId !== undefined && detailId >= 0) {
      var selected = items.find((s) => s.id === detailId);
      if (selected) {
        setSelectedItem(selected);
      }
    }
  }, [items, selectedItem, detailId]);

  function clearRequestHistory() {
    dataFetcher.invoke("clearTrafficHistory");
  }
  function exportRequestHistory() {
    const harObject = convertToHAR(items);

    // Convert the HAR object to a JSON string
    const harString = JSON.stringify(harObject, null, 2);

    // Download the HAR file
    const blob = new Blob([harString], { type: "application/json" });
    const link = document.createElement("a");
    const url = URL.createObjectURL(blob);

    link.href = url;
    link.setAttribute("download", "export.har");
    document.body.appendChild(link);
    link.click();

    document.body.removeChild(link);
  }
  const overviewPanel = (
    <table className="table table-hover" aria-labelledby="tabelLabel">
      <thead>
        <tr>
          <th>Time</th>
          <th>Method</th>
          <th>URL</th>
          <th>Status</th>
        </tr>
      </thead>
      <tbody>
        {items.map((item) => (
          <tr
            key={(item.id ?? 0) + (item.code ?? 0) * 1000}
            className={item.unread ? "unread" : item === selectedItem ? "active" : ""}
            onClick={() => {
              item.unread = false;
              props.onUnreadChange(data.trafficHistory.filter((s) => s.unread).length);
              setSelectedItem(item);
            }}
          >
            <td>{moment(item.requestAtOffset).fromNow()}</td>
            <td className={(item?.code ?? 500) < 300 ? "text-success" : "text-warning"}>
              <b>{item.methodName}</b>
            </td>
            <td>{item.url}</td>
            <td>{item.code ?? "(Waiting for response)"}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
  const detailPanel = <Details item={selectedItem}></Details>;

  return (
    <div className="mx-4 d-flex flex-row server-container overflow-auto">
      <div className="table-container flex-fill d-flex flex-column">
        <div className="d-flex flex-row">
          <h5>All requests</h5>
          <div className="flex-fill"></div>
          <div className="flex-right">
            <Button appearance="subtle" size="large" onClick={() => exportRequestHistory()} icon={<ExportHistoryIcon />}>
              Export
            </Button>
          </div>
          <div className="flex-right">
            <Dialog open={openDialog} onOpenChange={(event, data) => setOpenDialog(data.open)}>
              <DialogTrigger disableButtonEnhancement>
                <Button appearance="subtle" size="large" icon={<ClearHistoryIcon />}>
                  Clear all requests
                </Button>
              </DialogTrigger>
              <DialogSurface>
                <DialogBody>
                  <DialogTitle>Clear the request history?</DialogTitle>
                  <DialogContent>Are you sure to clear the request history? It is unrecoverable.</DialogContent>
                  <DialogActions>
                    <DialogTrigger disableButtonEnhancement>
                      <Button appearance="secondary">Cancel</Button>
                    </DialogTrigger>
                    <Button
                      appearance="primary"
                      onClick={() => {
                        clearRequestHistory();
                        setOpenDialog(false);
                      }}
                    >
                      Yes
                    </Button>
                  </DialogActions>
                </DialogBody>
              </DialogSurface>
            </Dialog>
          </div>
        </div>
        {!data.ready ? (
          <p>
            <em>Loading...</em>
          </p>
        ) : (
          <ResizablePanel left={overviewPanel} right={detailPanel}></ResizablePanel>
        )}
      </div>
    </div>
  );
}

function Details({ item }: { item?: HttpHistoryItem }) {
  if (!item) return <></>;
  let requestTabItems = [
    {
      title: "Request Details",
      content: (
        <div className="m-2" style={{ whiteSpace: "pre-wrap" }}>
          {item.requestRaw}
        </div>
      ),
    },
  ];
  let responseTabItems = [
    {
      title: "Response Details",
      content: (
        <div className="m-2" style={{ whiteSpace: "pre-wrap" }}>
          {item.responseRaw}
        </div>
      ),
    },
  ];
  return (
    <div className="panel-container d-flex flex-column flex-fill">
      <div className="banner d-flex">
        <span>{moment(item.requestAtOffset).fromNow()}</span>
      </div>
      <div className="request">
        <h5>
          {item.methodName} {item.url}
        </h5>
        <ReadonlyTabs items={requestTabItems}></ReadonlyTabs>
      </div>
      <div className="response">
        <h5 className={(item?.code ?? 500) < 300 ? "text-success" : "text-warning"}>{item.code}</h5>
        <ReadonlyTabs items={responseTabItems}></ReadonlyTabs>
      </div>
    </div>
  );
}

interface HarCreator {
  name: string;
  version: string;
}

interface HarBrowser {
  name: string;
  version: string;
}

interface HarPageTiming {
  onLoad: number;
  comment?: string;
}

interface HarPage {
  startedDateTime: string;
  id: string;
  title: string;
  pageTimings: HarPageTiming;
}

interface HarHeader {
  name: string;
  value: string;
}

interface HarQueryString {
  name: string;
  value: string;
}

interface HarRequest {
  method: string;
  url: string;
  httpVersion: string;
  headers: HarHeader[];
  queryString: HarQueryString[];
}

interface HarContent {
  size: number;
  mimeType: string;
}

interface HarResponse {
  status: number;
  statusText: string;
  httpVersion: string;
  headers: HarHeader[];
  content: HarContent;
}

interface HarCache {}

interface HarTimings {
  blocked: number;
  dns: number;
  connect: number;
  send: number;
  wait: number;
  receive: number;
  ssl: number;
  comment?: string;
}

interface HarEntry {
  startedDateTime: string;
  time: number;
  request: HarRequest;
  response: HarResponse;
  cache?: HarCache;
  timings?: HarTimings;
  comment?: string;
}

interface HarLog {
  version: string;
  creator: HarCreator;
  browser?: HarBrowser;
  pages?: HarPage[];
  entries: HarEntry[];
  comment?: string;
}

interface HarFile {
  log: HarLog;
}
