import express from "express";
import { WebPubSubEventHandler } from "@azure/web-pubsub-express";

function startMockServer(port: number, hub: string, path: string = "/eventhandler") {
  const handler = new WebPubSubEventHandler(hub, {
    path: path,
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
  
  app.listen(port, () => console.log(`Mock server ready at http://localhost:${port}${handler.path}`));
  
}
