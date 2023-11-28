import React from "react";
import * as signalR from "@microsoft/signalr";

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


  return (<iframe className="flex-fill" src={url} title="Live trace"></iframe>);
}

