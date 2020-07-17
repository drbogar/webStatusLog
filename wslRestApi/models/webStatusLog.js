const mongoose = require('mongoose');
const MongooseSchema = mongoose.Schema;

const webStatusLogSchema = new MongooseSchema({
    "Timestamp": {type: Date},
    "Guid": {type: String},
    "Connection": {
            "Type":{type: String},
            "ConnectedToMac":{type: String}
        },
    "Speeds":{
        "Download":{type: String},
        "Upload":{type: String}
    },
});
 
const WebStatusLog = mongoose.model('webStatusLog', webStatusLogSchema);

module.exports = WebStatusLog;