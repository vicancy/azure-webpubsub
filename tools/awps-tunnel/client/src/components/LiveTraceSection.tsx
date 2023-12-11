import React from "react";
import * as signalR from "@microsoft/signalr";

FolderRegular,
EditRegular,
OpenRegular,
DocumentRegular,
PeopleRegular,
DocumentPdfRegular,
VideoRegular,
} from "@fluentui/react-icons";
import {
PresenceBadgeStatus,
Avatar,
DataGridBody,
DataGridRow,
DataGrid,
DataGridHeader,
DataGridHeaderCell,
DataGridCell,
TableCellLayout,
TableColumnDefinition,
createTableColumn,
} from "@fluentui/react-components";

type FileCell = {
label: string;
icon: JSX.Element;
};

type LastUpdatedCell = {
label: string;
timestamp: number;
};

type LastUpdateCell = {
label: string;
icon: JSX.Element;
};

type AuthorCell = {
label: string;
status: PresenceBadgeStatus;
};

type Item = {
file: FileCell;
author: AuthorCell;
lastUpdated: LastUpdatedCell;
lastUpdate: LastUpdateCell;
};

const items: Item[] = [
{
  file: { label: "Meeting notes", icon: <DocumentRegular /> },
  author: { label: "Max Mustermann", status: "available" },
  lastUpdated: { label: "7h ago", timestamp: 1 },
  lastUpdate: {
    label: "You edited this",
    icon: <EditRegular />,
  },
},
{
  file: { label: "Thursday presentation", icon: <FolderRegular /> },
  author: { label: "Erika Mustermann", status: "busy" },
  lastUpdated: { label: "Yesterday at 1:45 PM", timestamp: 2 },
  lastUpdate: {
    label: "You recently opened this",
    icon: <OpenRegular />,
  },
},
{
  file: { label: "Training recording", icon: <VideoRegular /> },
  author: { label: "John Doe", status: "away" },
  lastUpdated: { label: "Yesterday at 1:45 PM", timestamp: 2 },
  lastUpdate: {
    label: "You recently opened this",
    icon: <OpenRegular />,
  },
},
{
  file: { label: "Purchase order", icon: <DocumentPdfRegular /> },
  author: { label: "Jane Doe", status: "offline" },
  lastUpdated: { label: "Tue at 9:30 AM", timestamp: 3 },
  lastUpdate: {
    label: "You shared this in a Teams chat",
    icon: <PeopleRegular />,
  },
},
];

const columns: TableColumnDefinition<Item>[] = [
createTableColumn<Item>({
  columnId: "file",
  compare: (a, b) => {
    return a.file.label.localeCompare(b.file.label);
  },
  renderHeaderCell: () => {
    return "File";
  },
  renderCell: (item) => {
    return (
      <TableCellLayout media={item.file.icon}>
        {item.file.label}
      </TableCellLayout>
    );
  },
}),
createTableColumn<Item>({
  columnId: "author",
  compare: (a, b) => {
    return a.author.label.localeCompare(b.author.label);
  },
  renderHeaderCell: () => {
    return "Author";
  },
  renderCell: (item) => {
    return (
      <TableCellLayout
        media={
          <Avatar
            aria-label={item.author.label}
            name={item.author.label}
            badge={{ status: item.author.status }}
          />
        }
      >
        {item.author.label}
      </TableCellLayout>
    );
  },
}),
createTableColumn<Item>({
  columnId: "lastUpdated",
  compare: (a, b) => {
    return a.lastUpdated.timestamp - b.lastUpdated.timestamp;
  },
  renderHeaderCell: () => {
    return "Last updated";
  },

  renderCell: (item) => {
    return item.lastUpdated.label;
  },
}),
createTableColumn<Item>({
  columnId: "lastUpdate",
  compare: (a, b) => {
    return a.lastUpdate.label.localeCompare(b.lastUpdate.label);
  },
  renderHeaderCell: () => {
    return "Last update";
  },
  renderCell: (item) => {
    return (
      <TableCellLayout media={item.lastUpdate.icon}>
        {item.lastUpdate.label}
      </TableCellLayout>
    );
  },
}),
];

export function LiveTraceSection({ url, tokenGenerator} : {url: string, tokenGenerator}) {

const connect = () =>{
const connection = new signalR.HubConnectionBuilder().withUrl(url, {
  accessTokenFactory: tokenGenerator,
  skipNegotiation: true,
  transport: signalR.HttpTransportType.WebSockets,
}).withAutomaticReconnect({
  nextRetryDelayInMilliseconds: () => 3000,
})
.configureLogging(signalR.LogLevel.Information)
.build();

function onReceivedLogEvent(logEvent) {
  if (!logEvent) return

  if (logEvent.eventId in logProps) {
      addLogDataToTable(logEvent, logProps)
  } else {
      logEventsToShow.push(logEvent)
  }
}

function onReceivedLogProps(props) {
  logProps[props.eventId] = props
  logEventsToShow = logEventsToShow
      .filter(data => data != null && data.eventId in logProps) // number of eventID is pretty limited
      .map(data => {
          addLogDataToTable(data, logProps)
          data = null
      })
}

connection.on("logEvent", event => {
  onReceivedLogEvent(event)
  if (!(event.eventId in logProps)) {
      connection.send("LogProperty", event.eventId)
  }
})
connection.on("LogProperty", props => {
  onReceivedLogProps(props)
})
connection.onclose(err => {
  state.value = connection.state
  console.error(err)
})
connection.onreconnected(() => {
  state.value = connection.state
})
connection
  .start()
  .then(() => {
      state.value = connection.state
      console.log('Get connected to service.')
      startListeningToLogEvents()
      console.log('Start listening to live traces')
  })
  .catch(err => {
      state.value = connection.state
      console.error(err)
  })
}


  return (<iframe className="flex-fill" src={url} title="Live trace"></iframe>)

  
return (
  <DataGrid
    items={items}
    columns={columns}
    sortable
    selectionMode="multiselect"
    getRowId={(item) => item.file.label}
    onSelectionChange={(e, data) => console.log(data)}
    focusMode="composite"
  >
    <DataGridHeader>
      <DataGridRow selectionCell={{ "aria-label": "Select all rows" }}>
        {({ renderHeaderCell }) => (
          <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>
        )}
      </DataGridRow>
    </DataGridHeader>
    <DataGridBody<Item>>
      {({ item, rowId }) => (
        <DataGridRow<Item>
          key={rowId}
          selectionCell={{ "aria-label": "Select row" }}
        >
          {({ renderCell }) => (
            <DataGridCell>{renderCell(item)}</DataGridCell>
          )}
        </DataGridRow>
      )}
    </DataGridBody>
  </DataGrid>
);
}

