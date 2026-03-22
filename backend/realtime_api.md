# WebSockets との接続
```python
# example requires websocket-client library:
# pip install websocket-client

import os
import json
import websocket

OPENAI_API_KEY = os.environ.get("OPENAI_API_KEY")

url = "wss://api.openai.com/v1/realtime?model=gpt-4o-realtime-preview-2024-12-17"
headers = [
    "Authorization: Bearer " + OPENAI_API_KEY,
    "OpenAI-Beta: realtime=v1"
]

def on_open(ws):
    print("Connected to server.")

def on_message(ws, message):
    data = json.loads(message)
    print("Received event:", json.dumps(data, indent=2))

ws = websocket.WebSocketApp(
    url,
    header=headers,
    on_open=on_open,
    on_message=on_message,
)

ws.run_forever()
```
# Audio inputs and outputs
One of the most powerful features of the Realtime API is voice-to-voice interaction with the model, without an intermediate text-to-speech or speech-to-text step. This enables lower latency for voice interfaces, and gives the model more data to work with around the tone and inflection of voice input.

## Voice options
Realtime sessions can be configured to use one of several built‑in voices when producing audio output. You can set the voice on session creation (or on a response.create) to control how the model sounds. Current voice options are alloy, ash, ballad, coral, echo, sage, shimmer, and verse. Once the model has emitted audio in a session, the voice cannot be modified for that session.

## Handling audio with WebSockets
When sending and receiving audio over a WebSocket, you will have a bit more work to do in order to send media from the client, and receive media from the server. Below, you'll find a table describing the flow of events during a WebSocket session that are necessary to send and receive audio over the WebSocket.

The events below are given in lifecycle order, though some events (like the delta events) may happen concurrently.

Lifecycle stage	Client events	Server events
Session initialization	
session.update

session.created

session.updated

User audio input	
conversation.item.create
  (send whole audio message)

input_audio_buffer.append
  (stream audio in chunks)

input_audio_buffer.commit
  (used when VAD is disabled)

response.create
  (used when VAD is disabled)

input_audio_buffer.speech_started

input_audio_buffer.speech_stopped

input_audio_buffer.committed

Server audio output	
input_audio_buffer.clear
  (used when VAD is disabled)

conversation.item.created

response.created

response.output_item.created

response.content_part.added

response.audio.delta

response.audio_transcript.delta

response.text.delta

response.audio.done

response.audio_transcript.done

response.text.done

response.content_part.done

response.output_item.done

response.done

rate_limits.updated

Streaming audio input to the server
To stream audio input to the server, you can use the input_audio_buffer.append client event. This event requires you to send chunks of Base64-encoded audio bytes to the Realtime API over the socket. Each chunk cannot exceed 15 MB in size.

The format of the input chunks can be configured either for the entire session, or per response.

Session: session.input_audio_format in session.update
Response: response.input_audio_format in response.create
```python
import base64
import json
import struct
import soundfile as sf
from websocket import create_connection

# ... create websocket-client named ws ...

def float_to_16bit_pcm(float32_array):
    clipped = [max(-1.0, min(1.0, x)) for x in float32_array]
    pcm16 = b''.join(struct.pack('<h', int(x * 32767)) for x in clipped)
    return pcm16

def base64_encode_audio(float32_array):
    pcm_bytes = float_to_16bit_pcm(float32_array)
    encoded = base64.b64encode(pcm_bytes).decode('ascii')
    return encoded

files = [
    './path/to/sample1.wav',
    './path/to/sample2.wav',
    './path/to/sample3.wav'
]

for filename in files:
    data, samplerate = sf.read(filename, dtype='float32')  
    channel_data = data[:, 0] if data.ndim > 1 else data
    base64_chunk = base64_encode_audio(channel_data)
    
    # Send the client event
    event = {
        "type": "input_audio_buffer.append",
        "audio": base64_chunk
    }
    ws.send(json.dumps(event))
```
# Create responses outside the default conversation
By default, all responses generated during a session are added to the session's conversation state (the "default conversation"). However, you may want to generate model responses outside the context of the session's default conversation, or have multiple responses generated concurrently. You might also want to have more granular control over which conversation items are considered while the model generates a response (e.g. only the last N number of turns).

Generating "out-of-band" responses which are not added to the default conversation state is possible by setting the response.conversation field to the string none when creating a response with the response.create client event.

When creating an out-of-band response, you will probably also want some way to identify which server-sent events pertain to this response. You can provide metadata for your model response that will help you identify which response is being generated for this client-sent event.
```python
prompt = """
Analyze the conversation so far. If it is related to support, output
"support". If it is related to sales, output "sales".
"""

event = {
    "type": "response.create",
    "response": {
        # Setting to "none" indicates the response is out of band,
        # and will not be added to the default conversation
        "conversation": "none",

        # Set metadata to help identify responses sent back from the model
        "metadata": { "topic": "classification" },

        # Set any other available response fields
        "modalities": [ "text" ],
        "instructions": prompt,
    },
}

ws.send(json.dumps(event))
```
Now, when you listen for the response.done server event, you can identify the result of your out-of-band response.
```python
def on_message(ws, message):
    server_event = json.loads(message)
    topic = ""

    # See if metadata is present
    try:
        topic = server_event.response.metadata.topic
    except AttributeError:
        print("topic not set")
    
    if server_event.type == "response.done" and topic == "classification":
        # this server event pertained to our OOB model response
        print(server_event.response.output[0])
```
## Create a custom context for responses
You can also construct a custom context that the model will use to generate a response, outside the default/current conversation. This can be done using the input array on a response.create client event. You can use new inputs, or reference existing input items in the conversation by ID.
```python
event = {
    "type": "response.create",
    "response": {
        "conversation": "none",
        "metadata": { "topic": "pizza" },
        "modalities": [ "text" ],

        # Create a custom input array for this request with whatever 
        # context is appropriate
        "input": [
            # potentially include existing conversation items:
            {
                "type": "item_reference",
                "id": "some_conversation_item_id"
            },

            # include new content as well
            {
                "type": "message",
                "role": "user",
                "content": [
                    {
                        "type": "input_text",
                        "text": "Is it okay to put pineapple on pizza?",
                    }
                ],
            }
        ],
    },
}

ws.send(json.dumps(event))
```
# Function calling
The Realtime models also support function calling, which enables you to execute custom code to extend the capabilities of the model. Here's how it works at a high level:

When updating the session or creating a response, you can specify a list of available functions for the model to call.
If when processing input, the model determines it should make a function call, it will add items to the conversation representing arguments to a function call.
When the client detects conversation items that contain function call arguments, it will execute custom code using those arguments
When the custom code has been executed, the client will create new conversation items that contain the output of the function call, and ask the model to respond.
Let's see how this would work in practice by adding a callable function that will provide today's horoscope to users of the model. We'll show the shape of the client event objects that need to be sent, and what the server will emit in turn.

## Configure callable functions
First, we must give the model a selection of functions it can call based on user input. Available functions can be configured either at the session level, or the individual response level.

Session: session.tools property in session.update
Response: response.tools property in response.create
Here's an example client event payload for a session.update that configures a horoscope generation function, that takes a single argument (the astrological sign for which the horoscope should be generated):

session.update
```python
{
  "type": "session.update",
  "session": {
    "tools": [
      {
        "type": "function",
        "name": "generate_horoscope",
        "description": "Give today's horoscope for an astrological sign.",
        "parameters": {
          "type": "object",
          "properties": {
            "sign": {
              "type": "string",
              "description": "The sign for the horoscope.",
              "enum": [
                "Aries",
                "Taurus",
                "Gemini",
                "Cancer",
                "Leo",
                "Virgo",
                "Libra",
                "Scorpio",
                "Sagittarius",
                "Capricorn",
                "Aquarius",
                "Pisces"
              ]
            }
          },
          "required": ["sign"]
        }
      }
    ],
    "tool_choice": "auto",
  }
}
```
The description fields for the function and the parameters help the model choose whether or not to call the function, and what data to include in each parameter. If the model receives input that indicates the user wants their horoscope, it will call this function with a sign parameter.

## Detect when the model wants to call a function
Based on inputs to the model, the model may decide to call a function in order to generate the best response. Let's say our application adds the following conversation item and attempts to generate a response:

conversation.item.create
```python
{
  "type": "conversation.item.create",
  "item": {
    "type": "message",
    "role": "user",
    "content": [
      {
        "type": "input_text",
        "text": "What is my horoscope? I am an aquarius."
      }
    ]
  }
}
```
Followed by a client event to generate a response:

response.create
```python
{
  "type": "response.create"
}
```
Instead of immediately returning a text or audio response, the model will instead generate a response that contains the arguments that should be passed to a function in the developer's application. You can listen for realtime updates to function call arguments using the response.function_call_arguments.delta server event, but response.done will also have the complete data we need to call our function.

response.done
```python
{
  "type": "response.done",
  "event_id": "event_AeqLA8iR6FK20L4XZs2P6",
  "response": {
    "object": "realtime.response",
    "id": "resp_AeqL8XwMUOri9OhcQJIu9",
    "status": "completed",
    "status_details": null,
    "output": [
      {
        "object": "realtime.item",
        "id": "item_AeqL8gmRWDn9bIsUM2T35",
        "type": "function_call",
        "status": "completed",
        "name": "generate_horoscope",
        "call_id": "call_sHlR7iaFwQ2YQOqm",
        "arguments": "{\"sign\":\"Aquarius\"}"
      }
    ],
    "usage": {
      "total_tokens": 541,
      "input_tokens": 521,
      "output_tokens": 20,
      "input_token_details": {
        "text_tokens": 292,
        "audio_tokens": 229,
        "cached_tokens": 0,
        "cached_tokens_details": { "text_tokens": 0, "audio_tokens": 0 }
      },
      "output_token_details": {
        "text_tokens": 20,
        "audio_tokens": 0
      }
    },
    "metadata": null
  }
}
```

In the JSON emitted by the server, we can detect that the model wants to call a custom function:

Property	Function calling purpose
response.output[0].type	When set to function_call, indicates this response contains arguments for a named function call.
response.output[0].name	The name of the configured function to call, in this case generate_horoscope
response.output[0].arguments	A JSON string containing arguments to the function. In our case, "{\"sign\":\"Aquarius\"}".
response.output[0].call_id	A system-generated ID for this function call - you will need this ID to pass a function call result back to the model.
Given this information, we can execute code in our application to generate the horoscope, and then provide that information back to the model so it can generate a response.

## Provide the results of a function call to the model
Upon receiving a response from the model with arguments to a function call, your application can execute code that satisfies the function call. This could be anything you want, like talking to external APIs or accessing databases.

Once you are ready to give the model the results of your custom code, you can create a new conversation item containing the result via the conversation.item.create client event.

conversation.item.create
```python
{
  "type": "conversation.item.create",
  "item": {
    "type": "function_call_output",
    "call_id": "call_sHlR7iaFwQ2YQOqm",
    "output": "{\"horoscope\": \"You will soon meet a new friend.\"}"
  }
}
```
The conversation item type is function_call_output
item.call_id is the same ID we got back in the response.done event above
item.output is a JSON string containing the results of our function call
Once we have added the conversation item containing our function call results, we again emit the response.create event from the client. This will trigger a model response using the data from the function call.

response.create
```
{
  "type": "response.create"
}
```