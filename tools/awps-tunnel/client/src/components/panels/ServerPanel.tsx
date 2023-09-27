import React from 'react';
import {
  makeStyles,
  shorthands,
  Tab,
  TabList,
} from "@fluentui/react-components";
import type { TabListProps } from "@fluentui/react-components";
import { Icon } from '@fluentui/react/lib/Icon';
import SyntaxHighlighter from 'react-syntax-highlighter';
import { docco } from 'react-syntax-highlighter/dist/esm/styles/hljs';
export interface ServerPanelProps {
  endpoint?: string;
}

const codeString:string = `import express from "express";
import { WebPubSubEventHandler } from "@azure/web-pubsub-express";

  const handler = new WebPubSubEventHandler(hub, {
    path: "/eventhandler",
    handleConnect: (req, res) => {
      res.success();
    },
    onConnected: (req) => {
      console.log("Connected");
    },
    onDisconnected: (req) => {
      console.log("Disconnected");
    },
    handleUserEvent: (req, res) => {
      console.log(JSON.stringify(req));
      res.success("Echo " + req.data, req.dataType);
    },
  });
  
  const app = express();
  
  app.use(handler.getMiddleware());
  
  app.listen(3000, () => console.log("server ready at http://localhost:3000/eventhandler"));
}`;
export function ServerPanel({ endpoint }: ServerPanelProps) {
  return (
    <p className="m-2">
      <Icon className="mx-2" iconName="ServerEnviroment"></Icon>
      <b>Requests are sending to your server: {endpoint}</b>
      
      <p>Web PubSub sends HTTP requests in <a>Cloud Events HTTP protocol</a> to your server.<br></br>
      Below code snippets shows how to handle these requests in your server.
      </p>
      
    <div>
      <TabList>
        <Tab value="tab1">Javascript</Tab>
      </TabList>
    <SyntaxHighlighter language="javascript" style={docco}>
      {codeString}
    </SyntaxHighlighter>
    </div>
    </p>
  );
}
